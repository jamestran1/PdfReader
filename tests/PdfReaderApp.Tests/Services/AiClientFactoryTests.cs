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
    public void ChatFactory_CreatesClient_ForClaude_WithoutNetwork()
    {
        using var client = new AiChatClientFactory()
            .Create(new AiClientOptions(AiProvider.Claude, "claude-sonnet-5-5", "sk-ant-test"));

        Assert.NotNull(client);
    }

    [Fact]
    public void ChatFactory_CreatesClient_ForGemini()
    {
        using var client = new AiChatClientFactory().Create(new AiClientOptions(AiProvider.Gemini, "gemini-2.5-flash", "test-key"));

        Assert.NotNull(client);
    }

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
    public void EmbeddingFactory_GeminiGenerator_ReportsConfiguredDimensions()
    {
        using var generator = new AiEmbeddingGeneratorFactory()
            .Create(new AiClientOptions(AiProvider.Gemini, "gemini-embedding-001", "test-key"));

        var metadata = generator.GetService<EmbeddingGeneratorMetadata>();

        // SDK 1.27.0 dùng DefaultModelDimensions làm OutputDimensionality trong request, nên giá trị báo cáo
        // chính là số chiều được yêu cầu (không quan sát được request thật nếu không gọi mạng).
        Assert.Equal(768, AiModelCatalog.GeminiEmbeddingDimensions);
        Assert.Equal(AiModelCatalog.GeminiEmbeddingDimensions, metadata?.DefaultModelDimensions);
        Assert.Equal("gemini-embedding-001", metadata?.DefaultModelId);
    }

    [Fact]
    public void EmbeddingFactory_Throws_ForClaudeWithoutEmbeddingApi() =>
        Assert.Throws<NotSupportedException>(() =>
            new AiEmbeddingGeneratorFactory().Create(new AiClientOptions(AiProvider.Claude, "m", "k")));
}
