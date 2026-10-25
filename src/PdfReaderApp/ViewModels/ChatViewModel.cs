using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Khung chat với AI: tin nhắn hiển thị, gửi tin (RAG + stream), nạp và lưu lịch sử theo owner.
/// Tách khỏi MainViewModel để test riêng; sở hữu và dispose <see cref="AiChatService"/>.</summary>
public sealed partial class ChatViewModel : ObservableObject, IDisposable
{
    private const int ContextPageWindow = 2;

    // Các nội dung AI KHÔNG phải câu trả lời thật (thông báo lỗi). Suy ra từ chính AiErrorMessages nên
    // tự cập nhật nếu đổi lời lỗi, cộng thông báo thiếu key theo từng provider.
    private static readonly HashSet<string> NonMemoryAiMessages = BuildNonMemoryAiMessages();

    private readonly AiChatService _chatService;
    private readonly IChatHistoryStore _chatHistory;
    private readonly RagContextService _ragContext;
    private readonly Func<ChatContext> _currentContext;
    private string? _currentChatOwnerId;
    private bool _isSending;

    [ObservableProperty]
    public partial string Input { get; set; } = string.Empty;

    /// <summary>Provider và model chat đang dùng, hiện ở đầu khung chat (vd "Anthropic Claude · claude-sonnet-5-5").</summary>
    [ObservableProperty]
    public partial string ActiveModelLabel { get; set; } = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public ChatViewModel(
        AiChatService chatService,
        IChatHistoryStore chatHistory,
        RagContextService ragContext,
        Func<ChatContext> currentContext)
    {
        _chatService = chatService;
        _chatHistory = chatHistory;
        _ragContext = ragContext;
        _currentContext = currentContext;
        RefreshActiveModel();
    }

    /// <summary>Đọc lại provider và model chat từ Settings; gọi sau khi lưu Settings.</summary>
    public void RefreshActiveModel() =>
        ActiveModelLabel = $"{AiModelCatalog.DisplayName(_chatService.ChatProvider)} · {_chatService.ChatModel}";

    /// <summary>Dựng ChatViewModel cùng các dịch vụ chat từ <paramref name="services"/>; ChatViewModel sở hữu chúng.</summary>
    public static ChatViewModel Create(MainViewModelServices services, Func<ChatContext> currentContext)
    {
        services.ChatHistory.EnsureSchema();
        return new ChatViewModel(
            new AiChatService(services.Settings, services.ChatClientFactory),
            services.ChatHistory,
            new RagContextService(services.DocumentIndex, services.EmbeddingFactory, services.Settings),
            currentContext);
    }

    // Nạp lại khung chat theo sách đang mở: hiện bong bóng cũ và dựng lại bộ nhớ LLM.
    // Sách chưa có lịch sử (hoặc chưa mở sách nào) -> hiện 1 bong bóng chào, reset LLM.
    public void LoadHistory()
    {
        string? newOwnerId = _currentContext().OwnerId;
        if (newOwnerId == _currentChatOwnerId && _currentChatOwnerId != null) return;
        _currentChatOwnerId = newOwnerId;

        Messages.Clear();

        if (newOwnerId is null)
        {
            ShowGreeting();
            _chatService.ResetConversation();
            return;
        }

        IReadOnlyList<ChatHistoryEntry> entries;
        try { entries = _chatHistory.GetAll(newOwnerId); }
#pragma warning disable CA1031 // degrade: đọc lịch sử chat lỗi thì coi như chưa có lịch sử
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ChatViewModel.LoadHistory: đọc lịch sử chat lỗi: {ex}"); entries = Array.Empty<ChatHistoryEntry>(); }
#pragma warning restore CA1031

        if (entries.Count == 0)
        {
            ShowGreeting();
            _chatService.ResetConversation();
            return;
        }

        // Hiển thị TẤT CẢ bong bóng để transcript trung thực, nhưng chỉ nạp các lượt
        // "dùng được" làm trí nhớ LLM (bỏ AI rỗng / báo lỗi / bị gián đoạn).
        foreach (var e in entries)
            Messages.Add(new ChatMessage { Role = e.Role, Content = e.Content });
        _chatService.SeedHistory(MemoryTurns(entries.Select(e => (e.Role, e.Content))));
    }

    /// <summary>Xoá lịch sử chat của một tài liệu (best-effort: lỗi store không chặn việc xoá tài liệu).</summary>
    public void DeleteHistoryFor(string documentId)
    {
#pragma warning disable CA1031 // best-effort: dọn lịch sử chat, lỗi không chặn việc xoá tài liệu
        try { _chatHistory.DeleteForDocument(documentId); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ChatViewModel.DeleteHistoryFor: xoá lịch sử chat lỗi: {ex}"); }
#pragma warning restore CA1031
    }

    private void ShowGreeting() => Messages.Add(new ChatMessage
    {
        Role = "AI",
        Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
    });

    // Lọc các lượt để nạp làm trí nhớ LLM. Giữ mọi lượt User; bỏ lượt AI rỗng, là thông báo
    // lỗi (suy ra từ AiErrorMessages), hoặc chứa sentinel gián đoạn. Bong bóng UI
    // vẫn hiển thị đầy đủ, chỉ trí nhớ LLM được làm sạch để AI không "nhớ nhầm" câu lỗi.
    internal static IEnumerable<(string role, string content)> MemoryTurns(
        IEnumerable<(string role, string content)> turns)
        => turns.Where(t => IsMemoryUsable(t.role, t.content));

    internal static bool IsMemoryUsable(string role, string content)
    {
        if (role != "AI") return true;
        if (string.IsNullOrEmpty(content)) return false;
        if (content.Contains(AiChatService.InterruptedSentinel, StringComparison.Ordinal)) return false;
        return !NonMemoryAiMessages.Contains(content);
    }

    private static HashSet<string> BuildNonMemoryAiMessages()
    {
        var set = new HashSet<string>(StringComparer.Ordinal) { AiErrorMessages.Unknown };
        foreach (AiProvider p in AiModelCatalog.ChatProviders)
            set.Add(AiErrorMessages.NotConfigured(p));
        foreach (AiChatError e in Enum.GetValues<AiChatError>())
            set.Add(AiErrorMessages.Describe(e));
        return set;
    }

    [RelayCommand]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(Input)) return;
        if (_isSending) return;
        _isSending = true;

        try
        {
            string question = Input;
            Input = string.Empty;
            Messages.Add(new ChatMessage { Role = "User", Content = question });

            if (!_chatService.IsConfigured)
            {
                string notConfigured = AiErrorMessages.NotConfigured(_chatService.ChatProvider);
                Messages.Add(new ChatMessage { Role = "AI", Content = notConfigured });
                PersistTurn(question, notConfigured);
                return;
            }

            string context = await BuildChatContextAsync(question);

            var aiMessage = new ChatMessage { Role = "AI", Content = string.Empty };
            Messages.Add(aiMessage);

            await StreamAnswerAsync(aiMessage, question, context);
            PersistTurn(question, aiMessage.Content);
        }
        finally
        {
            _isSending = false;
        }
    }

    private void PersistTurn(string question, string answer)
    {
        string? owner = _currentContext().OwnerId;
        if (owner is null) return;
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _chatHistory.Append(owner, "User", question, now);
            _chatHistory.Append(owner, "AI", answer, now);
        }
#pragma warning disable CA1031 // best-effort: lưu lịch sử lỗi không được chặn chat
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ChatViewModel.PersistTurn: lưu lịch sử chat lỗi: {ex}"); /* không chặn chat khi lưu lỗi */ }
#pragma warning restore CA1031
    }

    private async Task<string> BuildChatContextAsync(string question)
    {
        ChatContext context = _currentContext();
        string? rag = null;
        if (context.DocumentId is not null)
        {
            try { rag = await _ragContext.BuildContextAsync(context.DocumentId, question, ct: CancellationToken.None); }
#pragma warning disable CA1031 // degrade: RAG lỗi thì lùi về ngữ cảnh theo trang
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ChatViewModel.SendMessage: RAG lỗi: {ex}"); rag = null; }
#pragma warning restore CA1031
        }
        return rag ?? DocumentContextBuilder.BuildAround(context.Blocks, context.CurrentPage, ContextPageWindow);
    }

    private async Task StreamAnswerAsync(ChatMessage aiMessage, string question, string context)
    {
        try
        {
            await foreach (var token in _chatService.AskStreamingAsync(question, context, CancellationToken.None))
            {
                aiMessage.Content += token;
            }
        }
        catch (AiChatException ex)
        {
            aiMessage.Content = AiErrorMessages.Describe(ex.Error);
        }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: lỗi chat không lường trước thì hiện thông báo lỗi chung
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"ChatViewModel.SendMessage: lỗi chat không lường trước: {ex}");
            aiMessage.Content = AiErrorMessages.Unknown;
        }
#pragma warning restore CA1031
    }

    public void Dispose() => _chatService.Dispose();
}
