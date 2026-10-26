using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class RagContextServiceWorkspaceTests
{
    private static readonly string Model = AiModelSettings.Default.EmbeddingModel;

    private sealed class FakeSettings(string? key) : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => key;
        public void SaveApiKey(AiProvider provider, string a) { }
        public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(key);
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    private sealed class CountingFactory : IEmbeddingGeneratorFactory
    {
        public List<EmbeddingPurpose> Purposes { get; } = [];
        public int GenerateCalls { get; set; }
        public List<string> Questions { get; } = [];

        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose)
        {
            Purposes.Add(purpose);
            return new Gen(this);
        }

        private sealed class Gen(CountingFactory owner) : IEmbeddingGenerator<string, Embedding<float>>
        {
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions? o = null, CancellationToken ct = default)
            {
                owner.GenerateCalls++;
                var list = values.ToList();
                owner.Questions.AddRange(list);
                return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    list.Select(_ => new Embedding<float>(new float[] { 1f, 0f })).ToList()));
            }

            public object? GetService(Type t, object? k = null) => null;
            public void Dispose() { }
        }
    }

    private sealed class FakeIndex : IDocumentIndex
    {
        public Dictionary<(string Doc, string Model), DocumentIndexStatus> Statuses { get; } = [];
        public Dictionary<(string Doc, string Model), List<ScoredChunk>> Hits { get; } = [];
        public List<(string Doc, string Model, int K)> Queries { get; } = [];

        public bool VectorSearchAvailable => true;
        public bool TryBeginIndexing(string documentId, string embeddingModel) => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m)
            => Statuses.TryGetValue((d, m), out var s) ? s : DocumentIndexStatus.None;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c) => [];
        public IReadOnlyList<StoredChunk> GetChunks(string d) => [];
        public void DeleteEmbeddings(string d, string m) { }
        public void WriteEmbeddings(string m, IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { }
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, string m, float[] v, int k = 5) => [];
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string d, string m, float[] v, int k = 5)
        {
            Queries.Add((d, m, k));
            return Hits.TryGetValue((d, m), out var h) ? h : [];
        }
        public void Dispose() { }
    }

    private static ScoredChunk Hit(string doc, int page, string text, double distance)
        => new(new Chunk(doc, page, 0, text), distance);

    private static WorkspaceDocument Doc(string id, string title) => new(id, title, $"C:\\lib\\{id}.pdf");

    private static RagContextService Create(FakeIndex index, CountingFactory? factory = null, string? key = "sk-x")
        => new(index, factory ?? new CountingFactory(), new FakeSettings(key));

    private static FakeIndex TwoCompleteDocs()
    {
        var index = new FakeIndex();
        index.Statuses[("a", Model)] = DocumentIndexStatus.Complete;
        index.Statuses[("b", Model)] = DocumentIndexStatus.Complete;
        index.Hits[("a", Model)] = [Hit("a", 0, "a-near", 0.1), Hit("a", 4, "a-far", 0.9)];
        index.Hits[("b", Model)] = [Hit("b", 2, "b-mid", 0.4), Hit("b", 6, "b-farther", 1.2)];
        return index;
    }

    [Fact]
    public async Task BuildWorkspaceContext_MergesChunksAcrossDocumentsByDistance()
    {
        var svc = Create(TwoCompleteDocs());

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "Sách A"), Doc("b", "Sách B")], "câu hỏi");

        Assert.NotNull(result.Context);
        int near = result.Context!.IndexOf("a-near", StringComparison.Ordinal);
        int mid = result.Context.IndexOf("b-mid", StringComparison.Ordinal);
        int far = result.Context.IndexOf("a-far", StringComparison.Ordinal);
        int farther = result.Context.IndexOf("b-farther", StringComparison.Ordinal);
        Assert.True(near >= 0 && near < mid && mid < far && far < farther, result.Context);
    }

    [Fact]
    public async Task BuildWorkspaceContext_TakesGlobalTopK_NotKPerDocument()
    {
        var svc = Create(TwoCompleteDocs());

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "Sách A"), Doc("b", "Sách B")], "q", new WorkspaceRagOptions(TopK: 3));

        Assert.Equal(3, result.Sources.Count);
        Assert.Contains("b-mid", result.Context, StringComparison.Ordinal);
        Assert.DoesNotContain("b-farther", result.Context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildWorkspaceContext_NumbersSourcesWithTitleAndOneBasedPage()
    {
        var svc = Create(TwoCompleteDocs());

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "Sách A"), Doc("b", "Sách B")], "q");

        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 1\na-near", result.Context, StringComparison.Ordinal);
        Assert.Contains("[2] Tài liệu: \"Sách B\", Trang: 3\nb-mid", result.Context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildWorkspaceContext_ReturnsSourceTableMatchingNumbers()
    {
        var svc = Create(TwoCompleteDocs());

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "Sách A"), Doc("b", "Sách B")], "q");

        Assert.Equal(
            [
                new RagSource(1, "a", "Sách A", 0),
                new RagSource(2, "b", "Sách B", 2),
                new RagSource(3, "a", "Sách A", 4),
                new RagSource(4, "b", "Sách B", 6),
            ],
            result.Sources);
    }

    [Fact]
    public async Task BuildWorkspaceContext_EmbedsQuestionOnceWithQueryPurpose()
    {
        var factory = new CountingFactory();
        var svc = Create(TwoCompleteDocs(), factory);

        await svc.BuildWorkspaceContextAsync([Doc("a", "A"), Doc("b", "B")], "câu hỏi");

        Assert.Equal([EmbeddingPurpose.Query], factory.Purposes);
        Assert.Equal(1, factory.GenerateCalls);
        Assert.Equal(["câu hỏi"], factory.Questions);
    }

    [Fact]
    public async Task BuildWorkspaceContext_QueriesEveryCompleteDocumentOnlyForCurrentEmbeddingModel()
    {
        var index = TwoCompleteDocs();
        index.Statuses[("a", "other-model")] = DocumentIndexStatus.Complete;
        index.Hits[("a", "other-model")] = [Hit("a", 9, "OTHER-MODEL-VECTOR", 0.0)];
        var svc = Create(index);

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "A"), Doc("b", "B")], "q");

        Assert.All(index.Queries, q => Assert.Equal(Model, q.Model));
        Assert.Equal(["a", "b"], index.Queries.Select(q => q.Doc).OrderBy(d => d, StringComparer.Ordinal), StringComparer.Ordinal);
        Assert.DoesNotContain("OTHER-MODEL-VECTOR", result.Context, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BuildWorkspaceContext_DocumentCompleteOnlyForOtherModel_IsPendingAndNotQueried()
    {
        var index = new FakeIndex();
        index.Statuses[("a", "other-model")] = DocumentIndexStatus.Complete;
        index.Hits[("a", Model)] = [Hit("a", 0, "should-not-appear", 0.0)];
        var svc = Create(index);

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "A")], "q");

        Assert.Empty(index.Queries);
        Assert.Null(result.Context);
        Assert.Equal(["a"], result.UnindexedDocuments.Select(d => d.DocumentId), StringComparer.Ordinal);
    }

    [Fact]
    public async Task BuildWorkspaceContext_SkipsUnindexedDocumentsAndReportsThem()
    {
        var index = TwoCompleteDocs();
        index.Statuses[("c", Model)] = DocumentIndexStatus.None;
        index.Statuses[("d", Model)] = DocumentIndexStatus.Partial;
        index.Statuses[("e", Model)] = DocumentIndexStatus.Indexing;
        var svc = Create(index);

        var result = await svc.BuildWorkspaceContextAsync(
            [Doc("a", "A"), Doc("b", "B"), Doc("c", "C"), Doc("d", "D"), Doc("e", "E")], "q");

        Assert.Equal(["c", "d"], result.UnindexedDocuments.Select(d => d.DocumentId), StringComparer.Ordinal);
        Assert.Equal(["e"], result.IndexingDocuments.Select(d => d.DocumentId), StringComparer.Ordinal);
        Assert.DoesNotContain(index.Queries, q => q.Doc is "c" or "d" or "e");
        Assert.NotNull(result.Context);
    }

    [Fact]
    public async Task BuildWorkspaceContext_TextOnlyAndEmptyDocuments_AreNeitherQueriedNorPending()
    {
        var index = new FakeIndex();
        index.Statuses[("t", Model)] = DocumentIndexStatus.TextOnly;
        index.Statuses[("z", Model)] = DocumentIndexStatus.Empty;
        var svc = Create(index);

        var result = await svc.BuildWorkspaceContextAsync([Doc("t", "T"), Doc("z", "Z")], "q");

        Assert.Empty(index.Queries);
        Assert.Empty(result.UnindexedDocuments);
        Assert.Empty(result.IndexingDocuments);
    }

    [Fact]
    public async Task BuildWorkspaceContext_NoCompleteDocuments_DoesNotEmbedQuestion()
    {
        var factory = new CountingFactory();
        var svc = Create(new FakeIndex(), factory);

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "A")], "q");

        Assert.Null(result.Context);
        Assert.Empty(result.Sources);
        Assert.Equal(0, factory.GenerateCalls);
        Assert.Equal(["a"], result.UnindexedDocuments.Select(d => d.DocumentId), StringComparer.Ordinal);
    }

    [Fact]
    public async Task BuildWorkspaceContext_NoApiKey_ReturnsNoContextButStillReportsPending()
    {
        var index = TwoCompleteDocs();
        index.Statuses[("c", Model)] = DocumentIndexStatus.None;
        var svc = Create(index, key: null);

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "A"), Doc("c", "C")], "q");

        Assert.Null(result.Context);
        Assert.Empty(result.Sources);
    }

    [Fact]
    public async Task BuildWorkspaceContext_RespectsCharBudgetAndDropsSourcesBeyondIt()
    {
        var index = new FakeIndex();
        index.Statuses[("a", Model)] = DocumentIndexStatus.Complete;
        index.Hits[("a", Model)] =
        [
            Hit("a", 0, new string('x', 100), 0.1),
            Hit("a", 1, new string('y', 100), 0.2),
        ];
        var svc = Create(index);

        var result = await svc.BuildWorkspaceContextAsync([Doc("a", "A")], "q", new WorkspaceRagOptions(MaxChars: 150));

        Assert.Single(result.Sources);
        Assert.DoesNotContain("yyy", result.Context, StringComparison.Ordinal);
        Assert.True(result.Context!.Length <= 150);
    }

    [Fact]
    public async Task BuildWorkspaceContext_CancelledToken_Throws()
    {
        var svc = Create(TwoCompleteDocs());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.BuildWorkspaceContextAsync([Doc("a", "A")], "q", ct: cts.Token));
    }
}
