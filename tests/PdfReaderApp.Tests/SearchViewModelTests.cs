using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

public class SearchViewModelTests
{
    private sealed class StubIndex(Func<string, string, IReadOnlyList<SearchResult>> search) : IDocumentIndex
    {
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
}
