using Microsoft.Extensions.AI;
using OpenAI.Chat;

namespace PdfReaderApp.Services;

public sealed class OpenAiChatClientFactory : IChatClientFactory
{
    private const string Model = "gpt-4o-mini";

    public IChatClient Create(string apiKey) =>
        new ChatClient(Model, apiKey).AsIChatClient();
}
