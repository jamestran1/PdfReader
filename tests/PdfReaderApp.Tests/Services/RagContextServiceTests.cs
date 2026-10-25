using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class RagContextServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _k;
        public FakeSettings(string? k) => _k = k;
        public string? GetApiKey(AiProvider provider) => _k;
        public void SaveApiKey(AiProvider provider, string a) { }
        public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(_k);
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }
    private sealed class FakeGen : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[1536])).ToList()));
        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }
    private sealed class FakeFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => new FakeGen();
    }
    private sealed class StubIndex : IDocumentIndex
    {
        private readonly DocumentIndexStatus _status;
        private readonly List<Chunk> _retrieve;
        public StubIndex(DocumentIndexStatus s, List<Chunk> retrieve) { _status = s; _retrieve = retrieve; }
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m) => _status;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c) => [];
        public void WriteEmbeddings(IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { }
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, float[] v, int k = 5) => _retrieve;
        public void Dispose() { }
    }

    [Fact]
    public async Task BuildContext_Complete_ReturnsJoinedChunks()
    {
        var idx = new StubIndex(DocumentIndexStatus.Complete,
        [
            new("d", 0, 0, "first chunk"), new("d", 1, 1, "second chunk")
        ]);
        var svc = new RagContextService(idx, new FakeFactory(), new FakeSettings("sk-x"));

        var ctx = await svc.BuildContextAsync("d", "câu hỏi");

        Assert.NotNull(ctx);
        Assert.Contains("first chunk", ctx);
        Assert.Contains("second chunk", ctx);
    }

    [Fact]
    public async Task BuildContext_NotComplete_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.TextOnly, []), new FakeFactory(), new FakeSettings("sk-x"));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }

    [Fact]
    public async Task BuildContext_NoKey_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.Complete, []), new FakeFactory(), new FakeSettings(null));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }

    [Fact]
    public async Task BuildContext_NoResults_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.Complete, []), new FakeFactory(), new FakeSettings("sk-x"));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }
}
