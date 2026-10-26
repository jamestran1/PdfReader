using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class WorkspaceIndexingServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => "sk-x";
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => true;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    private sealed class FakeFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose) => new Gen();

        private sealed class Gen : IEmbeddingGenerator<string, Embedding<float>>
        {
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(_ => new Embedding<float>(new float[1536])).ToList()));

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }

    private sealed class RecordingIndex : IDocumentIndex
    {
        public List<StoredChunk> Stored { get; set; } = [];
        public List<Chunk> Written { get; } = [];
        public Dictionary<string, DocumentIndexStatus> Statuses { get; } = [];

        public bool VectorSearchAvailable => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m)
            => Statuses.TryGetValue(d, out var s) ? s : DocumentIndexStatus.None;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c)
        {
            Written.AddRange(c);
            Stored = [.. c.Select((chunk, i) => new StoredChunk(i + 1, chunk.Text))];
            return [.. Stored.Select(s => s.ChunkId)];
        }
        public IReadOnlyList<StoredChunk> GetChunks(string d) => Stored;
        public void DeleteEmbeddings(string d, string m) { }
        public void WriteEmbeddings(string m, IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) => Statuses[d] = s;
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, string m, float[] v, int k = 5) => [];
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string d, string m, float[] v, int k = 5) => [];
        public bool TryBeginIndexing(string documentId, string embeddingModel) => true;
        public void Dispose() { }
    }

    private sealed class FakePageTextSource(IReadOnlyList<PageText> pages) : IPageTextSource
    {
        public List<string> Extracted { get; } = [];

        public IReadOnlyList<PageText> Extract(string filePath)
        {
            Extracted.Add(filePath);
            return pages;
        }
    }

    private static WorkspaceIndexingService Create(RecordingIndex index, FakePageTextSource source)
        => new(new DocumentIndexingService(index, new FakeFactory(), new FakeSettings()), index, source, new FakeSettings());

    private static readonly WorkspaceDocument Doc = new("doc1", "Sách 1", "C:\\lib\\doc1.pdf");

    [Fact]
    public async Task IndexDocument_DocumentNotOpenAndWithoutChunks_ExtractsTextFromFileThenIndexes()
    {
        var index = new RecordingIndex();
        var source = new FakePageTextSource([new PageText(0, "nội dung trang một")]);

        await Create(index, source).IndexDocumentAsync(Doc, CancellationToken.None);

        Assert.Equal([Doc.FilePath], source.Extracted);
        Assert.NotEmpty(index.Written);
        Assert.Equal(DocumentIndexStatus.Complete, index.Statuses[Doc.DocumentId]);
    }

    [Fact]
    public async Task IndexDocument_ChunksAlreadyStoredByAnotherModel_ReusesThemWithoutExtractingText()
    {
        var index = new RecordingIndex { Stored = [new StoredChunk(1, "đoạn đã có")] };
        var source = new FakePageTextSource([]);

        await Create(index, source).IndexDocumentAsync(Doc, CancellationToken.None);

        Assert.Empty(source.Extracted);
        Assert.Empty(index.Written);
        Assert.Equal(DocumentIndexStatus.Complete, index.Statuses[Doc.DocumentId]);
    }

    [Fact]
    public async Task IndexDocument_AlreadyCompleteForCurrentModel_DoesNothing()
    {
        var index = new RecordingIndex();
        index.Statuses[Doc.DocumentId] = DocumentIndexStatus.Complete;
        var source = new FakePageTextSource([new PageText(0, "x")]);

        await Create(index, source).IndexDocumentAsync(Doc, CancellationToken.None);

        Assert.Empty(source.Extracted);
        Assert.Empty(index.Written);
    }

    [Fact]
    public async Task IndexDocument_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(new RecordingIndex(), new FakePageTextSource([new PageText(0, "x")]))
                .IndexDocumentAsync(Doc, cts.Token));
    }
}
