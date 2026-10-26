using Google.GenAI.Types;
using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class GeminiTaskTypeEmbeddingGeneratorTests
{
    private sealed class CapturingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public EmbeddingGenerationOptions? Received { get; private set; }

        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
        {
            Received = options;
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[3])).ToList()));
        }

        public object? GetService(System.Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static async Task<(EmbeddingGenerationOptions Sent, EmbedContentConfig Config)> RunAsync(
        EmbeddingPurpose purpose, EmbeddingGenerationOptions? callerOptions = null)
    {
        var inner = new CapturingGenerator();
        using var generator = new GeminiTaskTypeEmbeddingGenerator(inner, purpose);

        await generator.GenerateAsync(["x"], callerOptions);

        var sent = Assert.IsType<EmbeddingGenerationOptions>(inner.Received);
        var config = Assert.IsType<EmbedContentConfig>(sent.RawRepresentationFactory!(generator));
        return (sent, config);
    }

    [Theory]
    [InlineData(EmbeddingPurpose.Document, "RETRIEVAL_DOCUMENT")]
    [InlineData(EmbeddingPurpose.Query, "RETRIEVAL_QUERY")]
    public async Task GenerateAsync_SetsTaskTypeFromPurpose(EmbeddingPurpose purpose, string expected)
    {
        var (_, config) = await RunAsync(purpose);

        Assert.Equal(expected, config.TaskType);
    }

    [Fact]
    public async Task GenerateAsync_PreservesDimensionsAndModelId()
    {
        var caller = new EmbeddingGenerationOptions { Dimensions = 768, ModelId = "gemini-embedding-001" };

        var (sent, _) = await RunAsync(EmbeddingPurpose.Document, caller);

        Assert.Equal(768, sent.Dimensions);
        Assert.Equal("gemini-embedding-001", sent.ModelId);
    }

    [Fact]
    public async Task GenerateAsync_DoesNotMutateCallerOptions()
    {
        var caller = new EmbeddingGenerationOptions { Dimensions = 768 };

        await RunAsync(EmbeddingPurpose.Query, caller);

        Assert.Null(caller.RawRepresentationFactory);
    }

    [Fact]
    public async Task GenerateAsync_KeepsCallerConfigAndFillsMissingTaskType()
    {
        var caller = new EmbeddingGenerationOptions
        {
            RawRepresentationFactory = _ => new EmbedContentConfig { Title = "tiêu đề" },
        };

        var (_, config) = await RunAsync(EmbeddingPurpose.Document, caller);

        Assert.Equal("tiêu đề", config.Title);
        Assert.Equal("RETRIEVAL_DOCUMENT", config.TaskType);
    }

    [Fact]
    public async Task GenerateAsync_KeepsTaskTypeAlreadySetByCaller()
    {
        var caller = new EmbeddingGenerationOptions
        {
            RawRepresentationFactory = _ => new EmbedContentConfig { TaskType = "SEMANTIC_SIMILARITY" },
        };

        var (_, config) = await RunAsync(EmbeddingPurpose.Query, caller);

        Assert.Equal("SEMANTIC_SIMILARITY", config.TaskType);
    }
}
