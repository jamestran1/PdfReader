using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class AiErrorMessagesTests
{
    [Theory]
    [InlineData(AiProvider.OpenAi, "Chưa có API key cho OpenAI. Mở Cài đặt AI để nhập key.")]
    [InlineData(AiProvider.Gemini, "Chưa có API key cho Google Gemini. Mở Cài đặt AI để nhập key.")]
    [InlineData(AiProvider.Claude, "Chưa có API key cho Anthropic Claude. Mở Cài đặt AI để nhập key.")]
    public void NotConfigured_NamesTheGivenProvider(AiProvider provider, string expected)
    {
        Assert.Equal(expected, AiErrorMessages.NotConfigured(provider));
    }

    [Fact]
    public void Describe_ProviderErrors_AreNeutral()
    {
        foreach (AiChatError error in Enum.GetValues<AiChatError>())
        {
            string message = AiErrorMessages.Describe(error);
            Assert.DoesNotContain("OpenAI", message, StringComparison.Ordinal);
            Assert.DoesNotContain("Claude", message, StringComparison.Ordinal);
            Assert.DoesNotContain("Gemini", message, StringComparison.Ordinal);
        }
    }
}
