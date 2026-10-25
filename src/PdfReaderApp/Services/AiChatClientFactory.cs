using Anthropic;
using Microsoft.Extensions.AI;
using OpenAI.Chat;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiChatClientFactory : IChatClientFactory
{
    // API Anthropic bắt buộc có max_tokens; OpenAI không cần nên không đụng tới ChatOptions chung.
    private const int ClaudeDefaultMaxOutputTokens = 4096;

    public IChatClient Create(AiClientOptions options) => options.Provider switch
    {
        AiProvider.OpenAi => new ChatClient(options.Model, options.ApiKey).AsIChatClient(),
        AiProvider.Gemini => throw new NotSupportedException("Chat qua Google Gemini chưa được hỗ trợ."),
        AiProvider.Claude => new AnthropicClient { ApiKey = options.ApiKey }
            .AsIChatClient(options.Model, ClaudeDefaultMaxOutputTokens),
        _ => throw new NotSupportedException("Nhà cung cấp AI không được hỗ trợ."),
    };
}
