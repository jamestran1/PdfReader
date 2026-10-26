using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

public class SearchViewModelTests
{
    private sealed class StubIndex(Func<string, string, IReadOnlyList<SearchResult>> search) : IDocumentIndex
    {
        public bool VectorSearchAvailable => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => DocumentIndexStatus.None;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks) => [];
        public IReadOnlyList<StoredChunk> GetChunks(string documentId) => [];
        public void DeleteEmbeddings(string documentId, string embeddingModel) { }
        public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel) { }
        public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50) => search(documentId, query);
        public IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5) => [];
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string documentId, string embeddingModel, float[] queryVector, int k = 5) => [];
        public bool TryBeginIndexing(string documentId, string embeddingModel) => true;
        public void Dispose() { }
    }

    private static SearchViewModel Create(
        Func<string, string, IReadOnlyList<SearchResult>> search,
        string? documentId,
        List<int>? pages = null,
        List<string>? errors = null)
        => new(new StubIndex(search), () => documentId, p => pages?.Add(p), e => errors?.Add(e));

    [Fact]
    public void Search_FillsResultsAndExecutedQuery()
    {
        var hit = new SearchResult(2, "snip", 1);
        var vm = Create((_, _) => [hit], "doc1");
        vm.Query = "hành";

        vm.SearchCommand.Execute(null);

        Assert.Equal([hit], vm.Results);
        Assert.Equal("hành", vm.ExecutedQuery);
    }

    [Fact]
    public void Search_WithoutDocument_LeavesResultsEmpty()
    {
        var vm = Create((_, _) => [new SearchResult(0, "x", 1)], documentId: null);
        vm.Query = "abc";

        vm.SearchCommand.Execute(null);

        Assert.Empty(vm.Results);
        Assert.Equal(string.Empty, vm.ExecutedQuery);
    }

    [Fact]
    public void Search_WhenIndexThrows_ReportsError()
    {
        var errors = new List<string>();
        var vm = Create((_, _) => throw new InvalidOperationException("hỏng"), "doc1", errors: errors);
        vm.Query = "abc";

        vm.SearchCommand.Execute(null);

        Assert.Equal(["Tìm kiếm lỗi: hỏng"], errors);
    }

    [Fact]
    public void SelectResult_GoesToOneBasedPageAndSetsHighlight()
    {
        var pages = new List<int>();
        var vm = Create((_, _) => [], "doc1", pages);
        vm.Query = "hành";

        vm.SelectResultCommand.Execute(new SearchResult(4, "snip", 1));

        Assert.Equal([5], pages);
        Assert.Equal("hành", vm.SelectedQuery);
    }

    [Fact]
    public void ClearResults_KeepsQuery()
    {
        var vm = Create((_, _) => [new SearchResult(0, "x", 1)], "doc1");
        vm.Query = "abc";
        vm.SearchCommand.Execute(null);

        vm.ClearResults();

        Assert.Empty(vm.Results);
        Assert.Equal("abc", vm.Query);
    }

    [Fact]
    public void Reset_ClearsQueryHighlightAndResults()
    {
        var vm = Create((_, _) => [new SearchResult(0, "x", 1)], "doc1");
        vm.Query = "abc";
        vm.SearchCommand.Execute(null);
        vm.SelectedQuery = "abc";

        vm.Reset();

        Assert.Equal(string.Empty, vm.Query);
        Assert.Equal(string.Empty, vm.SelectedQuery);
        Assert.Equal(string.Empty, vm.ExecutedQuery);
        Assert.Empty(vm.Results);
    }

    private static readonly SearchDocument[] TwoDocs =
        [new("docA", "Kinh A"), new("docB", "Sách B")];

    private static SearchViewModel CreateWorkspace(
        Func<string, string, IReadOnlyList<SearchResult>> search,
        string? currentDocumentId,
        List<(string DocumentId, int PageIndex)> jumps,
        List<int>? pages = null)
        => new(new StubIndex(search), () => currentDocumentId, p => pages?.Add(p), _ => { },
            new SearchWorkspaceContext(() => TwoDocs, (id, page) => jumps.Add((id, page))));

    [Fact]
    public async Task Search_DefaultScopeIsCurrentDocument_QueriesOnlyThatDocument()
    {
        var queried = new List<string>();
        var vm = CreateWorkspace((id, _) => { queried.Add(id); return [new SearchResult(1, "s", 1)]; }, "docA", []);
        vm.Query = "hành";

        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(DocumentScope.CurrentDocument, vm.Scope);
        Assert.Equal(["docA"], queried);
        Assert.All(vm.Results, r => Assert.Null(r.DocumentTitle));
        Assert.False(vm.ShowDocumentTitle);
    }

    [Fact]
    public async Task Search_WorkspaceScope_FansOutAndFillsDocumentTitle()
    {
        var vm = CreateWorkspace((id, _) => [new SearchResult(id == "docA" ? 2 : 7, "s", 1)], "docA", []);
        vm.Query = "hành";
        vm.Scope = DocumentScope.Workspace;
        await vm.SearchCommand.ExecuteAsync(null);

        Assert.Equal(
            [("docA", "Kinh A", 2), ("docB", "Sách B", 7)],
            vm.Results.Select(r => (r.DocumentId!, r.DocumentTitle!, r.PageIndex)));
        Assert.True(vm.ShowDocumentTitle);
    }

    [Fact]
    public async Task Search_WorkspaceScope_SkipsDocumentsWithoutIndexedText()
    {
        var vm = CreateWorkspace((id, _) => id == "docB" ? [new SearchResult(0, "s", 1)] : [], "docA", []);
        vm.Query = "hành";
        vm.Scope = DocumentScope.Workspace;
        await vm.SearchCommand.ExecuteAsync(null);

        var only = Assert.Single(vm.Results);
        Assert.Equal("docB", only.DocumentId);
    }

    [Fact]
    public void ChangingScope_WithQuery_ReSearchesAndDropsOldResults()
    {
        var vm = CreateWorkspace((id, _) => [new SearchResult(0, "s", 1)], "docA", []);
        vm.Query = "hành";
        vm.SearchCommand.Execute(null);
        Assert.Single(vm.Results);

        vm.SetScopeCommand.Execute(DocumentScope.Workspace);

        Assert.True(vm.IsWorkspaceScope);
        Assert.False(vm.IsCurrentDocumentScope);
    }

    [Fact]
    public void SelectResult_FromOtherDocument_JumpsToThatDocumentAndPage()
    {
        var jumps = new List<(string DocumentId, int PageIndex)>();
        var pages = new List<int>();
        var vm = CreateWorkspace((_, _) => [], "docA", jumps, pages);
        vm.Query = "hành";

        vm.SelectResultCommand.Execute(new SearchResult(4, "s", 1, "docB", "Sách B"));

        Assert.Equal([("docB", 4)], jumps);
        Assert.Empty(pages);
    }

    [Fact]
    public void SelectResult_FromCurrentDocumentInWorkspaceScope_JustGoesToPage()
    {
        var jumps = new List<(string DocumentId, int PageIndex)>();
        var pages = new List<int>();
        var vm = CreateWorkspace((_, _) => [], "docA", jumps, pages);

        vm.SelectResultCommand.Execute(new SearchResult(4, "s", 1, "docA", "Kinh A"));

        Assert.Empty(jumps);
        Assert.Equal([5], pages);
    }

    [Fact]
    public async Task Search_NewSearchCancelsOlderOne_OlderResultsAreDiscarded()
    {
        var firstStarted = new ManualResetEventSlim();
        var releaseFirst = new ManualResetEventSlim();
        int call = 0;
        var vm = CreateWorkspace((id, q) =>
        {
            if (q == "cũ")
            {
                firstStarted.Set();
                releaseFirst.Wait(TimeSpan.FromSeconds(5));
                return [new SearchResult(0, "cũ", 1)];
            }

            Interlocked.Increment(ref call);
            return [new SearchResult(1, "mới", 2)];
        }, "docA", []);
        vm.Scope = DocumentScope.Workspace;

        vm.Query = "cũ";
        var first = vm.SearchCommand.ExecuteAsync(null);
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        vm.Query = "mới";
        await vm.SearchCommand.ExecuteAsync(null);
        releaseFirst.Set();
        await first;

        Assert.All(vm.Results, r => Assert.Equal("mới", r.Snippet));
        Assert.Equal("mới", vm.ExecutedQuery);
    }
}
