using System.Globalization;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class DocumentIndexingServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _k;
        public FakeSettings(string? k) => _k = k;
        public string? GetApiKey(AiProvider provider) => _k;
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(_k);
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    private sealed class FakeEmbedFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => new FakeGen();
    }

    private sealed class CountingEmbedFactory : IEmbeddingGeneratorFactory
    {
        public int Created { get; private set; }
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options)
        {
            Created++;
            return new FakeGen();
        }
    }

    private sealed class ThrowingEmbedFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => new ThrowingGen();
    }

    private sealed class ThrowingGen : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("embedding failure");
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeGen : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = values.Select(_ =>
            {
                var v = new float[1536];
                v[0] = 1f;
                return new Embedding<float>(v);
            }).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class SyncProgress : IProgress<IndexingProgress>
    {
        public List<IndexingProgress> Reports { get; } = [];
        public void Report(IndexingProgress value) => Reports.Add(value);
    }

    // Records what the service asked the index to do.
    private sealed class RecordingIndex : IDocumentIndex
    {
        public List<Chunk> Written = [];
        public List<(long, float[])> Embeddings = [];
        public List<string> DeletedEmbeddingModels = [];
        public string? FinalStatusDoc;
        public DocumentIndexStatus FinalStatus;
        public DocumentIndexStatus StatusToReturn = DocumentIndexStatus.None;
        public Dictionary<string, DocumentIndexStatus> StatusByModel = [];
        public List<StoredChunk> Stored = [];

        public bool VectorAvailable = true;
        public bool VectorSearchAvailable => VectorAvailable;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) =>
            StatusByModel.TryGetValue(embeddingModel, out var s) ? s : StatusToReturn;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)
        {
            Written.AddRange(chunks);
            var ids = Enumerable.Range(1, chunks.Count).Select(i => (long)i).ToList();
            Stored = [.. ids.Select((id, i) => new StoredChunk(id, chunks[i].Text))];
            return ids;
        }
        public IReadOnlyList<StoredChunk> GetChunks(string documentId) => Stored;
        public void DeleteEmbeddings(string documentId, string embeddingModel) => DeletedEmbeddingModels.Add(embeddingModel);
        public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> e) => Embeddings.AddRange(e);
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)
        {
            FinalStatusDoc = documentId;
            if (status != DocumentIndexStatus.Indexing) FinalStatus = status;
            StatusByModel[embeddingModel] = status;
        }
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, string m, float[] v, int k = 5) => [];
        public void Dispose() { }
    }

    private static List<PageText> Pages(params string[] texts) =>
        texts.Select((t, i) => new PageText(i, t)).ToList();

    [Fact]
    public async Task IndexAsync_WithKey_WritesChunksEmbeddingsAndCompletes()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Pages("page zero text", "page one text"), null, CancellationToken.None);

        Assert.NotEmpty(index.Written);
        Assert.Equal(index.Written.Count, index.Embeddings.Count);
        Assert.Equal(DocumentIndexStatus.Complete, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_NoKey_WritesChunksButTextOnly()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings(null));

        await svc.IndexAsync("doc1", null, Pages("hello world"), null, CancellationToken.None);

        Assert.NotEmpty(index.Written);
        Assert.Empty(index.Embeddings);
        Assert.Equal(DocumentIndexStatus.TextOnly, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_NoText_SetsEmpty()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", null, [], null, CancellationToken.None);

        Assert.Equal(DocumentIndexStatus.Empty, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_ReportsProgress()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        var reports = new List<IndexingProgress>();
        var progress = new Progress<IndexingProgress>(p => reports.Add(p));

        await svc.IndexAsync("doc1", null, Pages(new string('a', 2000)), progress, CancellationToken.None);

        // Progress is captured asynchronously; allow the sync context to drain.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
    }

    [Fact]
    public async Task IndexAsync_EmbeddingThrows_SetsPartialAndRethrows()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new ThrowingEmbedFactory(), new FakeSettings("sk-x"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.IndexAsync("doc1", "a.pdf", Pages("some text"), null, CancellationToken.None));

        Assert.Equal(DocumentIndexStatus.Partial, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_AlreadyComplete_SkipsReindexing()
    {
        var index = new RecordingIndex { StatusToReturn = DocumentIndexStatus.Complete };
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Pages("page text"), null, CancellationToken.None);

        Assert.Empty(index.Written);    // did not re-write chunks
        Assert.Empty(index.Embeddings); // did not re-embed (no quota wasted)
    }

    [Fact]
    public async Task IndexAsync_MoreChunksThanBatchSize_EmbedsEveryChunkInOrder()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        var pages = Pages([.. Enumerable.Range(0, 250).Select(i => "page " + i.ToString(CultureInfo.InvariantCulture))]);

        await svc.IndexAsync("doc1", filePath: null, pages, progress: null, CancellationToken.None);

        Assert.Equal(250, index.Embeddings.Count);
        Assert.Equal(Enumerable.Range(1, 250).Select(i => (long)i), index.Embeddings.Select(e => e.Item1));
        Assert.Equal(DocumentIndexStatus.Complete, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_Success_ReportsIndexingPerBatchThenComplete()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        var progress = new SyncProgress();
        var pages = Pages([.. Enumerable.Range(0, 250).Select(i => "page " + i.ToString(CultureInfo.InvariantCulture))]);

        await svc.IndexAsync("doc1", filePath: null, pages, progress, CancellationToken.None);

        Assert.Equal(
            [
                new IndexingProgress(0, 250, "indexing"),
                new IndexingProgress(100, 250, "indexing"),
                new IndexingProgress(200, 250, "indexing"),
                new IndexingProgress(250, 250, "indexing"),
                new IndexingProgress(250, 250, "complete")
            ],
            progress.Reports);
    }

    [Fact]
    public async Task IndexAsync_AlreadyComplete_ReportsCompleteOnce()
    {
        var index = new RecordingIndex { StatusToReturn = DocumentIndexStatus.Complete };
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        var progress = new SyncProgress();

        await svc.IndexAsync("doc1", filePath: null, Pages("page text"), progress, CancellationToken.None);

        Assert.Equal([new IndexingProgress(0, 0, "complete")], progress.Reports);
    }

    [Fact]
    public async Task IndexAsync_NoKey_ReportsStartOnlyAndKeepsChunks()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings(""));
        var progress = new SyncProgress();

        await svc.IndexAsync("doc1", filePath: null, Pages("hello world"), progress, CancellationToken.None);

        Assert.Single(progress.Reports);
        Assert.Equal("indexing", progress.Reports[0].Status);
        Assert.Equal(DocumentIndexStatus.TextOnly, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_NoText_WritesNoEmbeddingsAndReportsNothing()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        var progress = new SyncProgress();

        await svc.IndexAsync("doc1", filePath: null, [], progress, CancellationToken.None);

        Assert.Empty(index.Embeddings);
        Assert.Empty(progress.Reports);
    }

    [Fact]
    public async Task IndexAsync_Cancelled_RethrowsAndDoesNotMarkPartialOrComplete()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => svc.IndexAsync("doc1", filePath: null, Pages("some text"), progress: null, cts.Token));

        Assert.NotEqual(DocumentIndexStatus.Partial, index.FinalStatus);
        Assert.NotEqual(DocumentIndexStatus.Complete, index.FinalStatus);
        Assert.Empty(index.Embeddings);
    }

    [Fact]
    public async Task IndexAsync_ChunksAlreadyStoredByAnotherModel_ReusesChunksAndEmbedsOnlyForCurrentModel()
    {
        var index = new RecordingIndex
        {
            Stored = [new StoredChunk(10, "đã lưu một"), new StoredChunk(11, "đã lưu hai")]
        };
        index.StatusByModel["gemini-embedding-001"] = DocumentIndexStatus.Complete;
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Pages("nội dung trang"), null, CancellationToken.None);

        Assert.Empty(index.Written);
        Assert.Equal([10L, 11L], index.Embeddings.Select(e => e.Item1));
        Assert.Equal(DocumentIndexStatus.Complete, index.StatusByModel[AiModelSettings.Default.EmbeddingModel]);
        Assert.Equal(DocumentIndexStatus.Complete, index.StatusByModel["gemini-embedding-001"]);
        Assert.Equal([AiModelSettings.Default.EmbeddingModel], index.DeletedEmbeddingModels);
    }

    [Fact]
    public async Task IndexAsync_SwitchingBackToCompleteModel_DoesNotCallEmbeddingGenerator()
    {
        var index = new RecordingIndex();
        index.StatusByModel[AiModelSettings.Default.EmbeddingModel] = DocumentIndexStatus.Complete;
        var factory = new CountingEmbedFactory();
        var svc = new DocumentIndexingService(index, factory, new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Pages("page text"), null, CancellationToken.None);

        Assert.Equal(0, factory.Created);
        Assert.Empty(index.Embeddings);
        Assert.Empty(index.DeletedEmbeddingModels);
    }

    [Fact]
    public async Task IndexAsync_PartialModel_ClearsOnlyThatModelsVectorsBeforeReembedding()
    {
        var index = new RecordingIndex { Stored = [new StoredChunk(1, "một")] };
        index.StatusByModel[AiModelSettings.Default.EmbeddingModel] = DocumentIndexStatus.Partial;
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Pages("một"), null, CancellationToken.None);

        Assert.Equal([AiModelSettings.Default.EmbeddingModel], index.DeletedEmbeddingModels);
        Assert.Single(index.Embeddings);
    }

    [Fact]
    public async Task IndexAsync_VectorSearchUnavailable_MarksTextOnlyAndDoesNotEmbed()
    {
        var index = new RecordingIndex { VectorAvailable = false, Stored = [new StoredChunk(1, "một")] };
        var factory = new CountingEmbedFactory();
        var svc = new DocumentIndexingService(index, factory, new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Pages("một"), null, CancellationToken.None);

        Assert.Equal(DocumentIndexStatus.TextOnly, index.StatusByModel[AiModelSettings.Default.EmbeddingModel]);
        Assert.Equal(0, factory.Created);
        Assert.Empty(index.Embeddings);
    }
}
