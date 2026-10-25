using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class ApiKeyFormatTests
{
    [Theory]
    [InlineData("sk-ant-oat01-xyz")]
    [InlineData("  sk-ant-oat01-xyz  ")]
    public void Validate_ClaudeSubscriptionToken_ReturnsMessage(string key)
    {
        Assert.Equal(ApiKeyFormat.ClaudeSubscriptionTokenMessage, ApiKeyFormat.Validate(AiProvider.Claude, key));
    }

    [Theory]
    [InlineData(AiProvider.Claude, "sk-ant-api03-xyz")]
    [InlineData(AiProvider.Claude, "")]
    [InlineData(AiProvider.OpenAi, "sk-ant-oat01-xyz")]
    [InlineData(AiProvider.Gemini, "AIza-anything")]
    public void Validate_OtherKeys_ReturnsNull(AiProvider provider, string key)
    {
        Assert.Null(ApiKeyFormat.Validate(provider, key));
    }
}
