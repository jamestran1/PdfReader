using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

/// <summary>Chính sách dựng Ngữ cảnh trả lời (#277): chọn đường theo Phạm vi, xếp hàng lập chỉ mục trước khi nhúng,
/// notice có kiểu và các đường lùi về cửa sổ trang.</summary>
public sealed class AnswerContextBuilderTests : IDisposable
{
    private static readonly string Model = AiModelSettings.Default.EmbeddingModel;
    private static readonly AiProvider EmbeddingProvider = AiModelSettings.Default.EmbeddingProvider;

    private sealed class FakeSettings(string? key) : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => key;
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(key);
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    // Ghi "embed" vào Events khi bị gọi để test thứ tự enqueue so với nhúng; ném nếu được yêu cầu.
    private sealed class FakeEmbeddingFactory(List<string> events) : IEmbeddingGeneratorFactory
    {
        public bool Throws { get; set; }

        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose)
        {
            events.Add("embed");
            return Throws ? throw new InvalidOperationException("embedding hỏng") : new Gen();
        }

        private sealed class Gen : IEmbeddingGenerator<string, Embedding<float>>
        {
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(_ => new Embedding<float>(new float[] { 1f, 0f })).ToList()));
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }

    private sealed class FakeQueue(List<string> events) : IIndexingQueue
    {
        public List<string> Enqueued { get; } = [];
        public HashSet<string> Queued { get; } = [];

        public void Enqueue(IReadOnlyList<WorkspaceDocument> documents)
        {
            events.Add("enqueue");
            Enqueued.AddRange(documents.Select(d => d.DocumentId));
            foreach (var d in documents) Queued.Add(d.DocumentId);
        }

        public bool IsQueued(string documentId) => Queued.Contains(documentId);
    }

    private sealed class FakeIndex : IDocumentIndex
    {
        public Dictionary<string, DocumentIndexStatus> Statuses { get; } = [];
        public Dictionary<string, List<ScoredChunk>> Hits { get; } = [];

        public bool VectorSearchAvailable => true;
        public bool TryBeginIndexing(string documentId, string embeddingModel, out long claimToken)
        {
            claimToken = 0;
            return true;
        }
        public void ReleaseIndexing(string documentId, string embeddingModel, long claimToken) { }
        public bool WriteEmbeddingsIfClaimed(string documentId, string embeddingModel, long claimToken, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) => true;
        public bool FinishIndexing(string documentId, string embeddingModel, long claimToken, DocumentIndexStatus status) => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m)
            => m == Model && Statuses.TryGetValue(d, out var s) ? s : DocumentIndexStatus.None;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c) => [];
        public IReadOnlyList<StoredChunk> GetChunks(string d) => [];
        public void DeleteEmbeddings(string d, string m) { }
        public void WriteEmbeddings(string m, IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { }
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, string m, float[] v, int k = 5)
            => Hits.TryGetValue(d, out var h) ? [.. h.Select(s => s.Chunk)] : [];
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string d, string m, float[] v, int k = 5)
            => Hits.TryGetValue(d, out var h) ? h : [];
        public void Dispose() { }
    }

    private readonly List<string> _events = [];
    private readonly FakeIndex _index = new();
    private readonly FakeQueue _queue;
    private readonly FakeEmbeddingFactory _embeddings;
    private string? _key = "sk-x";

    public AnswerContextBuilderTests()
    {
        _queue = new FakeQueue(_events);
        _embeddings = new FakeEmbeddingFactory(_events);
        _index.Statuses["a"] = DocumentIndexStatus.Complete;
        _index.Statuses["b"] = DocumentIndexStatus.Complete;
        _index.Hits["a"] = [Hit("a", 0, "nội dung A", 0.1)];
        _index.Hits["b"] = [Hit("b", 2, "nội dung B", 0.4)];
    }

    private static ScoredChunk Hit(string doc, int page, string text, double distance)
        => new(new Chunk(doc, page, 0, text), distance);

    private static WorkspaceDocument Doc(string id, string title) => new(id, title, $"C:\\lib\\{id}.pdf");

    private static TextBlock PageBlock(int page, string text) => new(text, 0, 0, 10, 10, 12, page, "P");

    private static DocumentSnapshot Snapshot(string? documentId = "a")
        => new(documentId, [PageBlock(0, "chữ trang một")], 1, "Sách A");

    private static readonly RagSource PageSource = new(1, "a", "Sách A", 0, "chữ trang một");

    private Task<AnswerContext> BuildAsync(
        DocumentScope scope, DocumentSnapshot? snapshot = null,
        Func<IReadOnlyList<WorkspaceDocument>>? loadWorkspaceDocuments = null, CancellationToken ct = default)
    {
        var builder = new AnswerContextBuilder(
            new RagContextService(_index, _embeddings, new FakeSettings(_key)), _queue);
        return builder.BuildAsync(
            new AnswerContextRequest("câu hỏi", scope, snapshot ?? Snapshot(), loadWorkspaceDocuments ?? (() => [])), ct);
    }

    private void AllowWorkspaceOf(params WorkspaceDocument[] documents)
    {
        _workspaceDocuments = documents;
    }

    private IReadOnlyList<WorkspaceDocument> _workspaceDocuments = [Doc("a", "Sách A"), Doc("b", "Sách B")];

    private Task<AnswerContext> BuildWorkspaceAsync(CancellationToken ct = default)
        => BuildAsync(DocumentScope.Workspace, loadWorkspaceDocuments: () => _workspaceDocuments, ct: ct);

    // Tài liệu này

    [Fact]
    public async Task CurrentDocument_Indexed_UsesRagContextWithNumberedSourcesAndNoNotice()
    {
        var context = await BuildAsync(DocumentScope.CurrentDocument);

        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 1\nnội dung A", context.Text, StringComparison.Ordinal);
        Assert.Equal([new RagSource(1, "a", "Sách A", 0, "nội dung A")], context.Sources);
        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task CurrentDocument_NotIndexed_FallsBackToNumberedPageWindowWithoutNotice()
    {
        _index.Statuses["a"] = DocumentIndexStatus.None;

        var context = await BuildAsync(DocumentScope.CurrentDocument);

        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 1\nchữ trang một", context.Text, StringComparison.Ordinal);
        Assert.Equal([PageSource], context.Sources);
        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task CurrentDocument_MissingEmbeddingKey_FallsBackToPageWindowWithoutNotice()
    {
        _key = null;

        var context = await BuildAsync(DocumentScope.CurrentDocument);

        Assert.Equal([PageSource], context.Sources);
        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task CurrentDocument_EmbeddingFails_FallsBackToPageWindowSilently()
    {
        _embeddings.Throws = true;

        var context = await BuildAsync(DocumentScope.CurrentDocument);

        Assert.Equal([PageSource], context.Sources);
        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task CurrentDocument_NoDocumentId_UsesUnnumberedBlockContextWithoutSources()
    {
        var context = await BuildAsync(DocumentScope.CurrentDocument, Snapshot(documentId: null));

        Assert.Contains("chữ trang một", context.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("[1]", context.Text, StringComparison.Ordinal);
        Assert.Empty(context.Sources);
        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task CurrentDocument_NeverQueuesIndexing()
    {
        _index.Statuses["a"] = DocumentIndexStatus.None;

        await BuildAsync(DocumentScope.CurrentDocument);

        Assert.Empty(_events);
    }

    // Toàn Workspace

    [Fact]
    public async Task Workspace_CompleteAndUnindexed_QueuesUnindexedAndAnswersFromCompleteOnly()
    {
        _index.Statuses["c"] = DocumentIndexStatus.None;
        AllowWorkspaceOf(Doc("a", "Sách A"), Doc("c", "Sách C"));

        var context = await BuildWorkspaceAsync();

        Assert.Equal(["c"], _queue.Enqueued);
        Assert.Contains("nội dung A", context.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Sách C", context.Text, StringComparison.Ordinal);
        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task Workspace_SeveralCompleteDocuments_MergesNumberedSourcesAcrossThem()
    {
        var context = await BuildWorkspaceAsync();

        Assert.Equal(
            [new RagSource(1, "a", "Sách A", 0, "nội dung A"), new RagSource(2, "b", "Sách B", 2, "nội dung B")],
            context.Sources);
    }

    [Fact]
    public async Task Workspace_DocumentIndexingOutsideTheQueue_NoticeCountsIt()
    {
        _index.Statuses["e"] = DocumentIndexStatus.Indexing;
        AllowWorkspaceOf(Doc("a", "Sách A"), Doc("e", "Sách E"));

        var context = await BuildWorkspaceAsync();

        Assert.Equal([new AnswerContextNotice.IndexingElsewhere(1)], context.Notices);
        Assert.Contains("nội dung A", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Workspace_DocumentIndexingInsideTheQueue_IsNotCountedAgain()
    {
        _index.Statuses["c"] = DocumentIndexStatus.Indexing;
        _queue.Queued.Add("c");
        AllowWorkspaceOf(Doc("a", "Sách A"), Doc("c", "Sách C"));

        var context = await BuildWorkspaceAsync();

        Assert.Empty(context.Notices);
    }

    [Fact]
    public async Task Workspace_MissingEmbeddingKey_NoticesKeyAndFallsBackToPageWindowWithoutQueueing()
    {
        _index.Statuses["a"] = DocumentIndexStatus.None;
        _index.Statuses["b"] = DocumentIndexStatus.None;
        _key = null;

        var context = await BuildWorkspaceAsync();

        Assert.Equal([new AnswerContextNotice.MissingEmbeddingKey(EmbeddingProvider)], context.Notices);
        Assert.Equal([PageSource], context.Sources);
        Assert.Empty(_events);
    }

    [Fact]
    public async Task Workspace_EmbeddingFails_QueuesBeforeEmbeddingAndNoticesPageOnly()
    {
        _index.Statuses["c"] = DocumentIndexStatus.None;
        AllowWorkspaceOf(Doc("a", "Sách A"), Doc("c", "Sách C"));
        _embeddings.Throws = true;

        var context = await BuildWorkspaceAsync();

        // Nhúng lỗi không được làm mất việc lập chỉ mục: enqueue phải xảy ra trước lần nhúng thất bại.
        Assert.Equal(["enqueue", "embed"], _events);
        Assert.Equal(["c"], _queue.Enqueued);
        Assert.Equal([new AnswerContextNotice.PageOnlyFallback()], context.Notices);
        Assert.Equal([PageSource], context.Sources);
    }

    [Fact]
    public async Task Workspace_RagFailsAfterElsewhereNotice_KeepsBothNoticesInOrder()
    {
        _index.Statuses["e"] = DocumentIndexStatus.Indexing;
        AllowWorkspaceOf(Doc("a", "Sách A"), Doc("e", "Sách E"));
        _embeddings.Throws = true;

        var context = await BuildWorkspaceAsync();

        Assert.Equal(
            [new AnswerContextNotice.IndexingElsewhere(1), new AnswerContextNotice.PageOnlyFallback()],
            context.Notices);
    }

    [Fact]
    public async Task Workspace_NothingComplete_NoticesPageOnlyFallsBackToPageWindowAndStillQueues()
    {
        _index.Statuses["a"] = DocumentIndexStatus.None;
        _index.Statuses["b"] = DocumentIndexStatus.None;

        var context = await BuildWorkspaceAsync();

        Assert.Equal([new AnswerContextNotice.PageOnlyFallback()], context.Notices);
        Assert.Equal([PageSource], context.Sources);
        Assert.Equal(["a", "b"], _queue.Enqueued);
    }

    [Fact]
    public async Task Workspace_NoDocumentIdAndRagFails_UsesUnnumberedBlockContext()
    {
        _embeddings.Throws = true;

        var context = await BuildAsync(
            DocumentScope.Workspace, Snapshot(documentId: null), () => _workspaceDocuments);

        Assert.Contains("chữ trang một", context.Text, StringComparison.Ordinal);
        Assert.Empty(context.Sources);
        Assert.Equal([new AnswerContextNotice.PageOnlyFallback()], context.Notices);
    }

    // Huỷ

    [Theory]
    [InlineData(DocumentScope.CurrentDocument)]
    [InlineData(DocumentScope.Workspace)]
    public async Task CancelledToken_ThrowsInsteadOfFallingBack(DocumentScope scope)
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => BuildAsync(scope, loadWorkspaceDocuments: () => _workspaceDocuments, ct: cts.Token));
    }

    [Fact]
    public async Task Workspace_LoadingDocumentsFails_FallsBackToPageWindowWithPageOnlyNotice()
    {
        var context = await BuildAsync(
            DocumentScope.Workspace,
            loadWorkspaceDocuments: () => throw new InvalidOperationException("đọc workspace lỗi"));

        Assert.Equal([PageSource], context.Sources);
        Assert.Equal([new AnswerContextNotice.PageOnlyFallback()], context.Notices);
    }

    [Fact]
    public async Task CurrentDocument_NeverLoadsWorkspaceDocuments()
    {
        int loads = 0;

        await BuildAsync(DocumentScope.CurrentDocument, loadWorkspaceDocuments: () => { loads++; return []; });

        Assert.Equal(0, loads);
    }

    public void Dispose() => _index.Dispose();
}
