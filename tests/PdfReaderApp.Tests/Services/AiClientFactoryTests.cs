using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiClientFactoryTests
{
    [Fact]
    public void ChatFactory_CreatesClient_ForOpenAi()
    {
        using var client = new AiChatClientFactory().Create(new AiClientOptions(AiProvider.OpenAi, "gpt-4o-mini", "sk-test"));

        Assert.NotNull(client);
    }

    [Fact]
    public void ChatFactory_CreatesClient_ForGemini()
    {
        using var client = new AiChatClientFactory().Create(new AiClientOptions(AiProvider.Gemini, "gemini-2.5-flash", "test-key"));

        Assert.NotNull(client);
    }

    [Fact]
    public void ChatFactory_Throws_ForClaudeNotYetSupported() =>
        Assert.Throws<NotSupportedException>(() =>
            new AiChatClientFactory().Create(new AiClientOptions(AiProvider.Claude, "m", "k")));

    [Fact]
    public void EmbeddingFactory_CreatesGenerator_ForOpenAi()
    {
        using var generator = new AiEmbeddingGeneratorFactory()
            .Create(new AiClientOptions(AiProvider.OpenAi, "text-embedding-3-small", "sk-test"));

        Assert.NotNull(generator);
    }

    [Fact]
    public void EmbeddingFactory_CreatesGenerator_ForGemini()
    {
        using var generator = new AiEmbeddingGeneratorFactory()
            .Create(new AiClientOptions(AiProvider.Gemini, "gemini-embedding-001", "test-key"));

        Assert.NotNull(generator);
    }

    [Fact]
    public void EmbeddingFactory_GeminiGenerator_RequestsConfiguredDimensions()
    {
        using var generator = new AiEmbeddingGeneratorFactory()
            .Create(new AiClientOptions(AiProvider.Gemini, "gemini-embedding-001", "test-key"));

        var metadata = generator.GetService<EmbeddingGeneratorMetadata>();

        Assert.Equal(768, AiModelCatalog.GeminiEmbeddingDimensions);
        Assert.Equal(AiModelCatalog.GeminiEmbeddingDimensions, metadata?.DefaultModelDimensions);
        Assert.Equal("gemini-embedding-001", metadata?.DefaultModelId);
    }

    [Fact]
    public void EmbeddingFactory_Throws_ForClaudeWithoutEmbeddingApi() =>
        Assert.Throws<NotSupportedException>(() =>
            new AiEmbeddingGeneratorFactory().Create(new AiClientOptions(AiProvider.Claude, "m", "k")));

    [Fact]
    public void OwningEmbeddingGenerator_DisposesOwnedResource_WhenDisposed()
    {
        var owned = new DisposeFlag();
        var generator = new OwningEmbeddingGenerator(new StubEmbeddingGenerator(), owned);

        generator.Dispose();

        Assert.True(owned.Disposed);
    }

    [Fact]
    public void OwningChatClient_DisposesOwnedResource_WhenDisposed()
    {
        var owned = new DisposeFlag();
        var client = new OwningChatClient(new StubChatClient(), owned);

        client.Dispose();

        Assert.True(owned.Disposed);
    }

    private sealed class DisposeFlag : IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class StubEmbeddingGenerator : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GeneratedEmbeddings<Embedding<float>>());

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class StubChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse());

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
