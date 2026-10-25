using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class EmbeddingNormalizationTests
{
    [Fact]
    public void Normalize_ReturnsUnitLengthVector()
    {
        float[] result = EmbeddingNormalizer.Normalize([3f, 4f]);

        Assert.Equal(0.6f, result[0], 5);
        Assert.Equal(0.8f, result[1], 5);
        Assert.Equal(1.0, Math.Sqrt(result.Sum(v => (double)v * v)), 5);
    }

    [Fact]
    public void Normalize_ZeroVector_StaysZeroWithoutNaN()
    {
        float[] result = EmbeddingNormalizer.Normalize([0f, 0f, 0f]);

        Assert.All(result, v => Assert.Equal(0f, v));
    }

    [Fact]
    public void Normalize_DoesNotMutateInput()
    {
        float[] input = [3f, 4f];

        _ = EmbeddingNormalizer.Normalize(input);

        Assert.Equal([3f, 4f], input);
    }

    [Fact]
    public async Task NormalizingGenerator_NormalizesEveryGeneratedVector()
    {
        using var generator = new NormalizingEmbeddingGenerator(new FixedGenerator([[3f, 4f], [0f, 2f]]));

        var result = await generator.GenerateAsync(["a", "b"]);

        Assert.Equal(2, result.Count);
        Assert.Equal(0.8f, result[0].Vector.Span[1], 5);
        Assert.Equal(1f, result[1].Vector.Span[1], 5);
    }

    private sealed class FixedGenerator(float[][] vectors) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(vectors.Select(v => new Embedding<float>(v))));

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
