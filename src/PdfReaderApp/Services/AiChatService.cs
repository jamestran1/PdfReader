using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public sealed class AiChatService : IDisposable
{
    private const string SystemPrompt =
        "Bạn là trợ lý đọc tài liệu PDF. Trả lời bằng tiếng Việt, ngắn gọn, " +
        "chỉ dựa trên nội dung tài liệu được cung cấp. Nếu chưa có tài liệu hoặc " +
        "nội dung không đủ, hãy nói rõ điều đó.";
    public const string InterruptedSentinel = " ...[lỗi: phản hồi bị gián đoạn]";
    private const int MaxContextChars = 48000;

    private readonly ISettingsService _settings;
    private readonly IChatClientFactory _factory;
    private readonly List<ChatMessage> _history = [];

    // Tăng mỗi khi hội thoại bị dựng lại; yêu cầu bị huỷ chỉ dọn lượt của mình nếu thế hệ chưa đổi,
    // tránh xoá nhầm lượt của tài liệu khác đã nạp sau khi đổi tài liệu.
    private int _conversationGeneration;

    private IChatClient? _client;
    private AiClientOptions? _clientOptions;

    public AiChatService(ISettingsService settings, IChatClientFactory chatClientFactory)
    {
        _settings = settings;
        _factory = chatClientFactory;
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public bool IsConfigured => _settings.HasApiKey(_settings.GetModelSettings().ChatProvider);

    public void ResetConversation()
    {
        _conversationGeneration++;
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    /// <summary>Dựng lại lịch sử hội thoại LLM từ các lượt đã lưu (nội dung sạch, không kèm
    /// khối ngữ cảnh tài liệu). Dùng khi mở lại một sách để AI nhớ tiếp mạch.</summary>
    public void SeedHistory(IEnumerable<(string role, string content)> turns)
    {
        _conversationGeneration++;
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        foreach (var (role, content) in turns)
        {
            var chatRole = role == "AI" ? ChatRole.Assistant : ChatRole.User;
            _history.Add(new ChatMessage(chatRole, content));
        }
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question, string documentContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = EnsureClient();
        int generation = _conversationGeneration;
        _history.Add(BuildUserTurn(question, documentContext));

        var assistant = new StringBuilder();
        var e = StartStream(client, ct);

        try
        {
            while (true)
            {
                var step = await ReadNextAsync(e, hasPartialReply: assistant.Length > 0, ct);

                if (step.Cancelled)
                {
                    // Huỷ: bỏ cả lượt (kể cả câu trả lời dở) khỏi trí nhớ LLM.
                    if (generation == _conversationGeneration) _history.RemoveAt(_history.Count - 1);
                    ct.ThrowIfCancellationRequested();
                }

                if (step.Fatal != null)
                {
                    _history.RemoveAt(_history.Count - 1);
                    throw step.Fatal;
                }

                if (step.Interrupted)
                {
                    assistant.Append(InterruptedSentinel);
                    _history.Add(new ChatMessage(ChatRole.Assistant, assistant.ToString()));
                    yield return InterruptedSentinel;
                    yield break;
                }

                if (!step.HasNext)
                    break;

                if (!string.IsNullOrEmpty(step.Text))
                {
                    assistant.Append(step.Text);
                    yield return step.Text;
                }
            }
        }
        finally
        {
            await e.DisposeAsync();
        }

        _history.Add(new ChatMessage(ChatRole.Assistant, assistant.ToString()));
    }

    /// <summary>Kết quả một lần đọc stream. Lỗi đọc được gói lại vì iterator không được yield
    /// bên trong khối try/catch; vòng lặp xử lý sau khi thoát khối catch.</summary>
    private readonly record struct StreamStep(bool HasNext, string? Text, bool Interrupted, AiChatException? Fatal, bool Cancelled = false);

    private IChatClient EnsureClient()
    {
        var models = _settings.GetModelSettings();
        string? key = _settings.GetApiKey(models.ChatProvider);
        if (string.IsNullOrEmpty(key))
            throw new AiChatException(AiChatError.Unauthorized, "Chưa cấu hình API key.");

        var options = new AiClientOptions(models.ChatProvider, models.ChatModel, key);
        if (_client is null || _clientOptions != options)
        {
            _client?.Dispose();
            _client = null;
            try
            {
                _client = _factory.Create(options);
            }
            catch (NotSupportedException ex)
            {
                throw new AiChatException(AiChatError.UnsupportedProvider, "Nhà cung cấp AI chưa được hỗ trợ.", ex);
            }

            _clientOptions = options;
        }

        return _client;
    }

    private static ChatMessage BuildUserTurn(string question, string documentContext)
    {
        string context = documentContext.Length > MaxContextChars
            ? documentContext[..MaxContextChars]
            : documentContext;
        return new ChatMessage(ChatRole.User, $"Ngữ cảnh tài liệu:\n{context}\n\nCâu hỏi: {question}");
    }

    private IAsyncEnumerator<ChatResponseUpdate> StartStream(IChatClient client, CancellationToken ct)
    {
        try
        {
            return client.GetStreamingResponseAsync(_history, options: null, ct)
                         .GetAsyncEnumerator(ct);
        }
#pragma warning disable CA1031 // ranh giới gọi AI: SDK ném nhiều kiểu lỗi, được phân loại (AiErrorClassifier) và bọc lại kèm inner exception
        catch (Exception ex)
        {
            _history.RemoveAt(_history.Count - 1);
            throw new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex);
        }
#pragma warning restore CA1031
    }

    private static async Task<StreamStep> ReadNextAsync(
        IAsyncEnumerator<ChatResponseUpdate> e, bool hasPartialReply, CancellationToken ct)
    {
        try
        {
            bool hasNext = await e.MoveNextAsync();
            return new StreamStep(hasNext, hasNext ? e.Current.Text : null, Interrupted: false, Fatal: null);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new StreamStep(HasNext: false, Text: null, Interrupted: false, Fatal: null, Cancelled: true);
        }
#pragma warning disable CA1031 // ranh giới stream AI: SDK ném nhiều kiểu lỗi, được phân loại (AiErrorClassifier) và bọc lại kèm inner exception
        catch (Exception ex)
        {
            return hasPartialReply
                ? new StreamStep(HasNext: false, Text: null, Interrupted: true, Fatal: null)
                : new StreamStep(HasNext: false, Text: null, Interrupted: false,
                    new AiChatException(AiErrorClassifier.Classify(ex), "Lỗi gọi dịch vụ AI.", ex));
        }
#pragma warning restore CA1031
    }

    public void Dispose() => _client?.Dispose();
}
