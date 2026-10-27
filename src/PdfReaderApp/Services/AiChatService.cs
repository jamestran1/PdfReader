using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.Extensions.AI;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiChatService : IDisposable
{
    private const string SystemPrompt =
        "Bạn là trợ lý đọc tài liệu PDF. Trả lời bằng tiếng Việt, ngắn gọn, " +
        "chỉ dựa trên nội dung tài liệu được cung cấp. Nếu chưa có tài liệu hoặc " +
        "nội dung không đủ, hãy nói rõ điều đó. Khi ngữ cảnh gồm các đoạn đánh số dạng [n] " +
        "(kèm tên tài liệu và trang), hãy trích dẫn nguồn bằng đúng số đó ngay sau ý lấy từ đoạn ấy, " +
        "và kèm câu nguyên văn bạn dựa vào theo dạng [n: \"câu\"]: chép đúng từng chữ và đúng ngôn ngữ của nguồn " +
        "(nguồn tiếng Anh thì câu trích bằng tiếng Anh dù bạn trả lời tiếng Việt), chỉ một câu, tối đa khoảng 30 từ. " +
        "Ý dựa vào nhiều câu thì trích nhiều lần, mỗi lần một câu, ví dụ [1: \"The system must log every request.\"][1: \"Logs are kept for 90 days.\"] " +
        "hoặc [2: \"Điều 5. Bên B chịu phạt 5%.\"]; không bịa số không có trong ngữ cảnh.";
    public const string InterruptedSentinel = " ...[lỗi: phản hồi bị gián đoạn]";
    private const int MaxContextChars = ContextLimits.MaxChars;

    private readonly ISettingsService _settings;
    private readonly IChatClientFactory _factory;
    private readonly List<ChatMessage> _history = [];

    private IChatClient? _client;
    private AiClientOptions? _clientOptions;

    public AiChatService(ISettingsService settings, IChatClientFactory chatClientFactory)
    {
        _settings = settings;
        _factory = chatClientFactory;
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    public AiProvider ChatProvider => _settings.GetModelSettings().ChatProvider;

    public string ChatModel => _settings.GetModelSettings().ChatModel;

    public bool IsConfigured => _settings.HasApiKey(_settings.GetModelSettings().ChatProvider);

    public void ResetConversation()
    {
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
    }

    /// <summary>Dựng lại lịch sử hội thoại LLM từ các lượt đã lưu (nội dung sạch, không kèm
    /// khối ngữ cảnh tài liệu). Dùng khi mở lại một sách để AI nhớ tiếp mạch.</summary>
    public void SeedHistory(IEnumerable<(string role, string content)> turns)
    {
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        foreach (var (role, content) in turns)
        {
            var chatRole = role == "AI" ? ChatRole.Assistant : ChatRole.User;
            _history.Add(new ChatMessage(chatRole, chatRole == ChatRole.Assistant ? CitationParser.StripQuotes(content) : content));
        }
    }

    public async IAsyncEnumerable<string> AskStreamingAsync(
        string question, string documentContext,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var client = EnsureClient();
        // Giữ tham chiếu để dọn đúng lượt của yêu cầu này: lượt của yêu cầu khác hoặc của tài liệu
        // khác có thể đã được thêm sau (continuation của yêu cầu bị huỷ chạy trễ).
        var userTurn = BuildUserTurn(question, documentContext);
        _history.Add(userTurn);

        var assistant = new StringBuilder();
        var e = StartStream(client, userTurn, ct);

        try
        {
            while (true)
            {
                var step = await ReadNextAsync(e, hasPartialReply: assistant.Length > 0, ct);

                if (step.Cancelled)
                {
                    // Huỷ: bỏ cả lượt (kể cả câu trả lời dở) khỏi trí nhớ LLM.
                    _history.Remove(userTurn);
                    ct.ThrowIfCancellationRequested();
                }

                if (step.Fatal != null)
                {
                    // Huỷ có thể làm SDK ném lỗi khác OCE (kết nối bị đóng): vẫn coi là huỷ, không báo lỗi.
                    ThrowIfCancelled(userTurn, ct);
                    _history.Remove(userTurn);
                    throw step.Fatal;
                }

                if (step.Interrupted)
                {
                    ThrowIfCancelled(userTurn, ct);
                    assistant.Append(InterruptedSentinel);
                    _history.Add(new ChatMessage(ChatRole.Assistant, CitationParser.StripQuotes(assistant.ToString())));
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

        ThrowIfCancelled(userTurn, ct);
        // Số [n] của lượt trước không khớp bảng nguồn của lượt mới, còn câu trích chỉ tốn ngân sách ký tự (#23).
        _history.Add(new ChatMessage(ChatRole.Assistant, CitationParser.StripQuotes(assistant.ToString())));
    }

    // Nhà cung cấp có thể kết thúc stream bình thường dù token đã bị huỷ: vẫn coi là huỷ và bỏ lượt.
    private void ThrowIfCancelled(ChatMessage userTurn, CancellationToken ct)
    {
        if (!ct.IsCancellationRequested) return;
        _history.Remove(userTurn);
        ct.ThrowIfCancellationRequested();
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

    private IAsyncEnumerator<ChatResponseUpdate> StartStream(IChatClient client, ChatMessage userTurn, CancellationToken ct)
    {
        try
        {
            // Chỉ danh sách gửi đi bị tỉa; _history đầy đủ vẫn giữ trong bộ nhớ.
            return client.GetStreamingResponseAsync(ChatHistoryTrimmer.Trim(_history), options: null, ct)
                         .GetAsyncEnumerator(ct);
        }
#pragma warning disable CA1031 // ranh giới gọi AI: SDK ném nhiều kiểu lỗi, được phân loại (AiErrorClassifier) và bọc lại kèm inner exception
        catch (Exception ex)
        {
            _history.Remove(userTurn);
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
