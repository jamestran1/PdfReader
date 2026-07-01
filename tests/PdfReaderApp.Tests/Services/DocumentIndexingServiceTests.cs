using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public string? GetApiKey() => _k;
        public void SaveApiKey(string apiKey) { }
        public bool HasApiKey() => !string.IsNullOrEmpty(_k);
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
    }

    private sealed class FakeEmbedFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) => new FakeGen();
    }

    private sealed class ThrowingEmbedFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) => new ThrowingGen();
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

    // Records what the service asked the index to do.
    private sealed class RecordingIndex : IDocumentIndex
    {
        public List<Chunk> Written = new();
        public List<(long, float[])> Embeddings = new();
        public string? FinalStatusDoc;
        public DocumentIndexStatus FinalStatus;
        public DocumentIndexStatus StatusToReturn = DocumentIndexStatus.None;

        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => StatusToReturn;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)
        {
            Written.AddRange(chunks);
            return Enumerable.Range(1, chunks.Count).Select(i => (long)i).ToList();
        }
        public void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> e) => Embeddings.AddRange(e);
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)
        { FinalStatusDoc = documentId; FinalStatus = status; }
        public List<SearchResult> SearchText(string d, string q, int l = 50) => new();
        public List<Chunk> RetrieveRelevant(string d, float[] v, int k = 5) => new();
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

        await svc.IndexAsync("doc1", null, new List<PageText>(), null, CancellationToken.None);

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
}
