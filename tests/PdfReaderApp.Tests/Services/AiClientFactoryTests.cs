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
    public void ChatFactory_CreatesClient_ForClaude_WithoutNetwork()
    {
        using var client = new AiChatClientFactory()
            .Create(new AiClientOptions(AiProvider.Claude, "claude-sonnet-5-5", "sk-ant-test"));

        Assert.NotNull(client);
    }

    [Fact]
    public void ChatFactory_Throws_ForGeminiNotYetSupported() =>
        Assert.Throws<NotSupportedException>(() =>
            new AiChatClientFactory().Create(new AiClientOptions(AiProvider.Gemini, "m", "k")));

    [Fact]
    public void EmbeddingFactory_CreatesGenerator_ForOpenAi()
    {
        using var generator = new AiEmbeddingGeneratorFactory()
            .Create(new AiClientOptions(AiProvider.OpenAi, "text-embedding-3-small", "sk-test"));

        Assert.NotNull(generator);
    }

    [Theory]
    [InlineData(AiProvider.Gemini)]
    [InlineData(AiProvider.Claude)]
    public void EmbeddingFactory_Throws_ForProvidersNotYetSupported(AiProvider provider) =>
        Assert.Throws<NotSupportedException>(() =>
            new AiEmbeddingGeneratorFactory().Create(new AiClientOptions(provider, "m", "k")));
}
