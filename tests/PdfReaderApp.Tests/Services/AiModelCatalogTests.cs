using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiModelCatalogTests
{
    [Theory]
    [InlineData(AiProvider.OpenAi, "gpt-4o-mini")]
    [InlineData(AiProvider.Gemini, "gemini-2.5-flash")]
    [InlineData(AiProvider.Claude, "claude-sonnet-5-5")]
    public void DefaultChatModel_ReturnsModelOfProvider(AiProvider provider, string expected) =>
        Assert.Equal(expected, AiModelCatalog.DefaultChatModel(provider));

    [Theory]
    [InlineData(AiProvider.OpenAi, "text-embedding-3-small")]
    [InlineData(AiProvider.Gemini, "gemini-embedding-001")]
    public void DefaultEmbeddingModel_ReturnsModelOfProvider(AiProvider provider, string expected) =>
        Assert.Equal(expected, AiModelCatalog.DefaultEmbeddingModel(provider));

    [Theory]
    [InlineData(AiProvider.OpenAi, "OpenAI")]
    [InlineData(AiProvider.Gemini, "Google Gemini")]
    [InlineData(AiProvider.Claude, "Anthropic Claude")]
    public void DisplayName_ReturnsReadableName(AiProvider provider, string expected) =>
        Assert.Equal(expected, AiModelCatalog.DisplayName(provider));

    [Fact]
    public void DefaultEmbeddingModel_Throws_ForClaude() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => AiModelCatalog.DefaultEmbeddingModel(AiProvider.Claude));

    [Fact]
    public void EmbeddingProviders_ExcludeClaude() =>
        Assert.DoesNotContain(AiProvider.Claude, AiModelCatalog.EmbeddingProviders);

    [Fact]
    public void ChatProviders_ContainAllThree() =>
        Assert.Equal([AiProvider.OpenAi, AiProvider.Gemini, AiProvider.Claude], AiModelCatalog.ChatProviders);

    [Fact]
    public void ModelSettingsDefault_MatchesCatalogOpenAiDefaults()
    {
        Assert.Equal(AiModelCatalog.DefaultChatModel(AiProvider.OpenAi), AiModelSettings.Default.ChatModel);
        Assert.Equal(AiModelCatalog.DefaultEmbeddingModel(AiProvider.OpenAi), AiModelSettings.Default.EmbeddingModel);
    }
}
