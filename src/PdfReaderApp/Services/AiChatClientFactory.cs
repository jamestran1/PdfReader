using Microsoft.Extensions.AI;
using OpenAI.Chat;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiChatClientFactory : IChatClientFactory
{
    public IChatClient Create(AiClientOptions options) => options.Provider switch
    {
        AiProvider.OpenAi => new ChatClient(options.Model, options.ApiKey).AsIChatClient(),
        AiProvider.Gemini => CreateGemini(options),
        AiProvider.Claude => throw new NotSupportedException("Chat qua Anthropic Claude chưa được hỗ trợ."),
        _ => throw new NotSupportedException("Nhà cung cấp AI không được hỗ trợ."),
    };

    private static OwningChatClient CreateGemini(AiClientOptions options)
    {
        var client = GeminiClientFactory.Create(options.ApiKey);
        return new OwningChatClient(client.AsIChatClient(options.Model), client);
    }
}
