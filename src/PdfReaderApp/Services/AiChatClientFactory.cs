using Microsoft.Extensions.AI;
using OpenAI.Chat;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiChatClientFactory : IChatClientFactory
{
    public IChatClient Create(AiClientOptions options) => options.Provider switch
    {
        AiProvider.OpenAi => new ChatClient(options.Model, options.ApiKey).AsIChatClient(),
        AiProvider.Gemini => throw new NotSupportedException("Chat qua Google Gemini chưa được hỗ trợ."),
        AiProvider.Claude => throw new NotSupportedException("Chat qua Anthropic Claude chưa được hỗ trợ."),
        _ => throw new NotSupportedException("Nhà cung cấp AI không được hỗ trợ."),
    };
}
