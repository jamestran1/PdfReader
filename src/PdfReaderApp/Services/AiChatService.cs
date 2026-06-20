using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public sealed class AiChatService
{
    private const string SystemPrompt =
        "Bạn là trợ lý đọc tài liệu PDF. Trả lời bằng tiếng Việt, ngắn gọn, " +
        "chỉ dựa trên nội dung tài liệu được cung cấp. Nếu chưa có tài liệu hoặc " +
        "nội dung không đủ, hãy nói rõ điều đó.";
    private const string InterruptedSentinel = " ...[lỗi: phản hồi bị gián đoạn]";
    private const int MaxContextChars = 48000;

    private readonly ISettingsService _settings;
    private readonly IChatClientFactory _factory;
    private readonly List<ChatMessage> _history = new();

    private IChatClient? _client;
    private string? _clientKey;

    public AiChatService(ISettingsService settings, IChatClientFactory chatClientFactory)
    {
        _settings = settings;
        _factory = chatClientFactory;
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public bool IsConfigured => _settings.HasApiKey();

    public void ResetConversation()
    {
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question, string documentContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        string? key = _settings.GetApiKey();
        if (string.IsNullOrEmpty(key))
            throw new AiChatException(AiChatError.Unauthorized, "Chưa cấu hình API key.");

        if (_client is null || _clientKey != key)
        {
            _client = _factory.Create(key);
            _clientKey = key;
        }

        string context = documentContext.Length > MaxContextChars
            ? documentContext[..MaxContextChars]
            : documentContext;
        _history.Add(new ChatMessage(ChatRole.User,
            $"Ngữ cảnh tài liệu:\n{context}\n\nCâu hỏi: {question}"));

        var assistant = new StringBuilder();

        IAsyncEnumerator<ChatResponseUpdate> e;
        try
        {
            e = _client.GetStreamingResponseAsync(_history, options: null, ct)
                       .GetAsyncEnumerator(ct);
        }
        catch (Exception ex)
        {
            throw new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex);
        }

        try
        {
            while (true)
            {
                bool hasNext = false;
                string? text = null;
                bool interrupted = false;
                AiChatException? fatal = null;

                try
                {
                    hasNext = await e.MoveNextAsync();
                    if (hasNext) text = e.Current.Text;
                }
                catch (Exception ex)
                {
                    if (assistant.Length > 0) interrupted = true;
                    else fatal = new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex);
                }

                if (fatal != null)
                    throw fatal;

                if (interrupted)
                {
                    assistant.Append(InterruptedSentinel);
                    _history.Add(new ChatMessage(ChatRole.Assistant, assistant.ToString()));
                    yield return InterruptedSentinel;
                    yield break;
                }

                if (!hasNext)
                    break;

                if (!string.IsNullOrEmpty(text))
                {
                    assistant.Append(text);
                    yield return text;
                }
            }
        }
        finally
        {
            await e.DisposeAsync();
        }

        _history.Add(new ChatMessage(ChatRole.Assistant, assistant.ToString()));
    }
}
