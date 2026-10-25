using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class OwningEmbeddingGeneratorTests
{
    [Fact]
    public void Dispose_DisposesOwnedResource()
    {
        var owned = new DisposeFlag();
        var generator = new OwningEmbeddingGenerator(new StubGenerator(), owned);

        generator.Dispose();

        Assert.True(owned.Disposed);
    }

    private sealed class DisposeFlag : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class StubGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
