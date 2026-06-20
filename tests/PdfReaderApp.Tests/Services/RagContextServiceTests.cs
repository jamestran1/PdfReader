using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
        public string? GetApiKey() => _k;
        public void SaveApiKey(string a) { }
        public bool HasApiKey() => !string.IsNullOrEmpty(_k);
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
        public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) => new FakeGen();
    }
    private sealed class StubIndex : IDocumentIndex
    {
        private readonly DocumentIndexStatus _status;
        private readonly List<Chunk> _retrieve;
        public StubIndex(DocumentIndexStatus s, List<Chunk> retrieve) { _status = s; _retrieve = retrieve; }
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m) => _status;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c) => new List<long>();
        public void WriteEmbeddings(IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { }
        public List<SearchResult> SearchText(string d, string q, int l = 50) => new();
        public List<Chunk> RetrieveRelevant(string d, float[] v, int k = 5) => _retrieve;
        public void Dispose() { }
    }

    [Fact]
    public async Task BuildContext_Complete_ReturnsJoinedChunks()
    {
        var idx = new StubIndex(DocumentIndexStatus.Complete, new List<Chunk>
        {
            new("d", 0, 0, "first chunk"), new("d", 1, 1, "second chunk")
        });
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
            new StubIndex(DocumentIndexStatus.TextOnly, new()), new FakeFactory(), new FakeSettings("sk-x"));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }

    [Fact]
    public async Task BuildContext_NoKey_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.Complete, new()), new FakeFactory(), new FakeSettings(null));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }

    [Fact]
    public async Task BuildContext_NoResults_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.Complete, new()), new FakeFactory(), new FakeSettings("sk-x"));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }
}
