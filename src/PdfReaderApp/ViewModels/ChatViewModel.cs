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
    private const string PageOnlyNotice =
        "Câu trả lời này chỉ dựa trên trang đang đọc, chưa dùng được các tài liệu khác của Workspace.";
    private const string MissingKeySuffix = " (chưa có key)";

    // Lời lỗi của các bản cũ vẫn nằm trong lịch sử chat đã lưu; giữ trong bộ lọc để không nạp lại vào trí nhớ LLM.
    private const string LegacyNotConfiguredMessage =
        "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key.";
    private const string LegacyUnsupportedProviderMessage =
        "Nhà cung cấp AI đã chọn chưa được hỗ trợ. Vui lòng chọn OpenAI hoặc Claude trong Cài đặt.";
    private const string LegacyUnknownErrorMessage = "Đã xảy ra lỗi khi gọi AI.";

    // Các nội dung AI KHÔNG phải câu trả lời thật (thông báo lỗi). Suy ra từ chính AiErrorMessages nên
    // tự cập nhật nếu đổi lời lỗi, cộng thông báo thiếu key theo từng provider.
    private static readonly HashSet<string> NonMemoryAiMessages = BuildNonMemoryAiMessages();

    private readonly AiChatService _chatService;
    private readonly IChatHistoryStore _chatHistory;
    private readonly RagContextService _ragContext;
    private readonly Func<ChatContext> _currentContext;
    private readonly ChatWorkspace _workspace;
    private readonly CitationOpener _openSource;
    private string? _currentChatOwnerId;
    // Yêu cầu chat đang chạy (RAG + stream). Mỗi yêu cầu tự dispose CTS của mình; trường này chỉ để huỷ.
    private CancellationTokenSource? _requestCts;
    // Hoàn tất của lượt gửi gần nhất (không bao giờ lỗi): lượt mới chờ nó xong để lịch sử LLM sạch
    // và không có hai yêu cầu cùng sửa lịch sử.
    private Task _lastSend = Task.CompletedTask;

    [ObservableProperty]
    public partial string Input { get; set; } = string.Empty;

    /// <summary>Provider và model chat đang dùng, hiện ở đầu khung chat (vd "Anthropic Claude · claude-sonnet-5-5"); thêm "(chưa có key)" khi thiếu key.</summary>
    [ObservableProperty]
    public partial string ActiveModelLabel { get; set; } = string.Empty;

    /// <summary>Phạm vi hỏi đáp: Tài liệu này (mặc định, RAG một tài liệu) hoặc Toàn Workspace (RAG xuyên các Document).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorkspaceScope))]
    [NotifyPropertyChangedFor(nameof(IsCurrentDocumentScope))]
    public partial DocumentScope Scope { get; set; } = DocumentScope.CurrentDocument;

    public bool IsWorkspaceScope => Scope == DocumentScope.Workspace;

    public bool IsCurrentDocumentScope => Scope == DocumentScope.CurrentDocument;

    /// <summary>Tiến độ lập chỉ mục nền các tài liệu Workspace chưa được câu trả lời dùng tới.</summary>
    public WorkspaceIndexingRunner WorkspaceIndexing => _workspace.Indexing;

    /// <summary>Thông báo trên ô nhập: lập chỉ mục nền, thiếu key, hoặc câu trả lời chỉ dùng trang đang đọc.</summary>
    public string Notice => string.Join('\n', new[] { ScopeNotice, WorkspaceIndexing.StatusText }.Where(s => s.Length > 0));

    // Cảnh báo của lần hỏi Toàn Workspace gần nhất (thiếu key embedding, hoặc phải lùi về trang đang đọc).
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Notice))]
    public partial string ScopeNotice { get; set; } = string.Empty;

    partial void OnScopeChanged(DocumentScope value) => ScopeNotice = string.Empty;

    public ObservableCollection<ChatMessage> Messages { get; } = [];

    public ChatViewModel(
        AiChatService chatService,
        IChatHistoryStore chatHistory,
        RagContextService ragContext,
        Func<ChatContext> currentContext,
        ChatWorkspace workspace,
        CitationOpener? openSource = null)
    {
        _openSource = openSource ?? ((_, _) => { });
        _chatService = chatService;
        _chatHistory = chatHistory;
        _ragContext = ragContext;
        _currentContext = currentContext;
        _workspace = workspace;
        _workspace.Indexing.PropertyChanged += (_, _) => OnPropertyChanged(nameof(Notice));
        RefreshActiveModel();
    }

    // Radio-style như Search: bấm segment luôn đặt phạm vi, không bỏ chọn phạm vi hiện tại.
    [RelayCommand]
    private void SetScope(DocumentScope scope)
    {
        Scope = scope;
    }

    /// <summary>Bấm một trích dẫn <c>[n]</c> trong câu trả lời: Cross-doc jump tới Document và trang được trích, tô đoạn nguồn.</summary>
    [RelayCommand]
    private void OpenCitation(CitationSpan citation)
    {
        if (citation.Source is { } source) _openSource(source, citation.Quote);
    }

    /// <summary>Đọc lại provider và model chat từ Settings; gọi sau khi lưu Settings.</summary>
    public void RefreshActiveModel() =>
        ActiveModelLabel = $"{AiModelCatalog.DisplayName(_chatService.ChatProvider)} · {_chatService.ChatModel}"
            + (_chatService.IsConfigured ? string.Empty : MissingKeySuffix);

    // Đưa về luồng UI không chặn luồng nền (InvokeAsync, không phải Invoke); thứ tự các lần gọi được giữ.
    private static void PostToApplicationDispatcher(Action action)
    {
        _ = System.Windows.Application.Current?.Dispatcher.InvokeAsync(action);
    }

    /// <summary>Dựng ChatViewModel cùng các dịch vụ chat từ <paramref name="services"/>; ChatViewModel sở hữu chúng.</summary>
    /// <param name="services">Các dịch vụ dùng chung của ứng dụng.</param>
    /// <param name="currentContext">Ảnh chụp trạng thái tài liệu đang mở mỗi lần nạp lịch sử hoặc gửi tin.</param>
    /// <param name="getWorkspaceDocuments">Document thuộc Workspace đang mở, cho hỏi đáp Toàn Workspace.</param>
    /// <param name="openSource">Mở nguồn khi bấm trích dẫn <c>[n]</c>: nhảy tới Document, trang và tô đoạn nguồn.</param>
    public static ChatViewModel Create(
        MainViewModelServices services, Func<ChatContext> currentContext,
        Func<IReadOnlyList<WorkspaceDocument>> getWorkspaceDocuments,
        CitationOpener openSource)
    {
        services.ChatHistory.EnsureSchema();
        var workspaceIndexing = new WorkspaceIndexingService(
            new DocumentIndexingService(services.DocumentIndex, services.EmbeddingFactory, services.Settings),
            services.DocumentIndex, new PdfFilePageTextSource(), services.Settings);
        return new ChatViewModel(
            new AiChatService(services.Settings, services.ChatClientFactory),
            services.ChatHistory,
            new RagContextService(services.DocumentIndex, services.EmbeddingFactory, services.Settings),
            currentContext,
            new ChatWorkspace(getWorkspaceDocuments, new WorkspaceIndexingRunner(workspaceIndexing.IndexDocumentAsync, PostToApplicationDispatcher)),
            openSource);
    }

    // Nạp lại khung chat theo sách đang mở: hiện bong bóng cũ và dựng lại bộ nhớ LLM.
    // Sách chưa có lịch sử (hoặc chưa mở sách nào) -> hiện 1 bong bóng chào, reset LLM.
    public void LoadHistory()
    {
        string? newOwnerId = _currentContext().OwnerId;
        if (newOwnerId == _currentChatOwnerId && _currentChatOwnerId != null) return;
        _currentChatOwnerId = newOwnerId;

        // Đổi tài liệu: câu trả lời đang chạy thuộc tài liệu cũ, không được ghi sang tài liệu mới.
        CancelPendingRequest();
        _workspace.Indexing.Cancel();
        ScopeNotice = string.Empty;
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
            Messages.Add(new ChatMessage { Role = e.Role, Content = e.Content, Sources = e.Sources });
        _chatService.SeedHistory(MemoryTurns(entries.Select(e => (e.Role, e.Content))));
    }

    /// <summary>Huỷ yêu cầu chat đang chạy (nếu có): khi đổi tài liệu, đóng tab hoặc gửi câu hỏi mới.
    /// Yêu cầu bị huỷ không hiện lỗi và không được lưu vào lịch sử.</summary>
    public void CancelPendingRequest() => _requestCts?.Cancel();

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
        var set = new HashSet<string>(StringComparer.Ordinal)
        {
            AiErrorMessages.Unknown,
            LegacyNotConfiguredMessage,
            LegacyUnsupportedProviderMessage,
            LegacyUnknownErrorMessage,
        };
        foreach (AiProvider p in AiModelCatalog.ChatProviders)
            set.Add(AiErrorMessages.NotConfigured(p));
        foreach (AiChatError e in Enum.GetValues<AiChatError>())
            set.Add(AiErrorMessages.Describe(e));
        return set;
    }

    // Cho chạy đồng thời để nút gửi/Enter vẫn dùng được khi đang trả lời: câu hỏi mới huỷ câu cũ.
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task SendMessageAsync()
    {
        if (string.IsNullOrWhiteSpace(Input)) return;

        // Câu hỏi mới thay thế yêu cầu đang chạy (CS-13).
        Task previous = _lastSend;
        CancelPendingRequest();
        using var cts = new CancellationTokenSource();
        _requestCts = cts;
        var done = new TaskCompletionSource();
        _lastSend = done.Task;
        // Chốt chủ sở hữu lịch sử lúc gửi: đổi tài liệu giữa chừng không được đổi nơi lưu.
        string? owner = _currentContext().OwnerId;
        ChatMessage? aiMessage = null;

        try
        {
            string question = Input;
            Input = string.Empty;
            Messages.Add(new ChatMessage { Role = "User", Content = question });

            // Chờ yêu cầu cũ (đã bị huỷ) dọn xong lượt của nó rồi mới dựng prompt mới.
            await previous;
            cts.Token.ThrowIfCancellationRequested();

            if (!_chatService.IsConfigured)
            {
                string notConfigured = AiErrorMessages.NotConfigured(_chatService.ChatProvider);
                Messages.Add(new ChatMessage { Role = "AI", Content = notConfigured });
                PersistTurn(owner, question, notConfigured);
                return;
            }

            BuiltContext built = await BuildChatContextAsync(question, cts.Token);

            aiMessage = new ChatMessage { Role = "AI", Content = string.Empty, Sources = built.Sources };
            Messages.Add(aiMessage);

            await StreamAnswerAsync(aiMessage, question, built.Text, cts.Token);
            PersistTurn(owner, question, aiMessage.Content, aiMessage.Sources);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // Huỷ có chủ đích: không báo lỗi, không lưu lượt này. Câu trả lời dở (nếu còn hiển thị
            // vì bị thay bởi câu hỏi mới) giữ nguyên để người dùng thấy, nhưng bong bóng rỗng thì bỏ.
            if (aiMessage is { Content.Length: 0 }) Messages.Remove(aiMessage);
        }
        finally
        {
            if (ReferenceEquals(_requestCts, cts)) _requestCts = null;
            done.SetResult();
        }
    }

    private void PersistTurn(string? owner, string question, string answer, IReadOnlyList<RagSource>? sources = null)
    {
        if (owner is null) return;
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            _chatHistory.Append(owner, "User", question, now);
            _chatHistory.Append(owner, "AI", answer, now, sources);
        }
#pragma warning disable CA1031 // best-effort: lưu lịch sử lỗi không được chặn chat
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ChatViewModel.PersistTurn: lưu lịch sử chat lỗi: {ex}"); /* không chặn chat khi lưu lỗi */ }
#pragma warning restore CA1031
    }

    // Ngữ cảnh gửi model và bảng nguồn [n] đi kèm (rỗng khi lùi về ngữ cảnh theo trang).
    private readonly record struct BuiltContext(string Text, IReadOnlyList<RagSource> Sources);

    private async Task<BuiltContext> BuildChatContextAsync(string question, CancellationToken ct)
    {
        ChatContext context = _currentContext();
        BuiltContext? rag = null;
        ScopeNotice = string.Empty;
        try
        {
            rag = Scope == DocumentScope.Workspace
                ? await BuildWorkspaceRagAsync(question, ct)
                : await BuildSingleDocumentRagAsync(context, question, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
#pragma warning disable CA1031 // degrade: RAG lỗi thì lùi về ngữ cảnh theo trang
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"ChatViewModel.SendMessage: RAG lỗi: {ex}");
            if (Scope == DocumentScope.Workspace) AppendScopeNotice(PageOnlyNotice);
        }
#pragma warning restore CA1031
        return rag ?? new BuiltContext(
            DocumentContextBuilder.BuildAround(context.Blocks, context.CurrentPage, ContextPageWindow), []);
    }

    private async Task<BuiltContext?> BuildSingleDocumentRagAsync(ChatContext context, string question, CancellationToken ct)
    {
        if (context.DocumentId is null) return null;
        // FilePath chỉ dùng để lập chỉ mục nền, việc này không có ở phạm vi Tài liệu này.
        var document = new WorkspaceDocument(context.DocumentId, context.DocumentTitle, FilePath: string.Empty);
        RagContext? rag = await _ragContext.BuildDocumentContextAsync(document, question, ct: ct);
        return rag is null ? null : new BuiltContext(rag.Text, rag.Sources);
    }

    private void AppendScopeNotice(string notice)
        => ScopeNotice = ScopeNotice.Length == 0 ? notice : $"{ScopeNotice} {notice}";

    // Câu hỏi không chờ tài liệu chưa index: chỉ dùng tài liệu đã Complete, phần còn lại đưa vào lập chỉ mục nền.
    private async Task<BuiltContext?> BuildWorkspaceRagAsync(string question, CancellationToken ct)
    {
        var plan = _ragContext.PlanWorkspaceIndexing(_workspace.GetDocuments());

        // Xếp hàng lập chỉ mục TRƯỚC khi nhúng câu hỏi: nhúng lỗi (mạng, key) không được làm mất việc lập chỉ mục.
        if (plan.Unindexed.Count > 0) _workspace.Indexing.Enqueue(plan.Unindexed);

        // Tài liệu đang lập chỉ mục ở nơi khác (vd tài liệu đang mở) chỉ được nêu một lần cho câu hỏi này, không giữ lâu dài:
        // không có sự kiện nào báo khi chúng xong nên một con số lưu lại sẽ cũ đi. Tài liệu runner đang giữ claim thì đã có
        // trong StatusText của runner nên không đếm lại.
        int elsewhere = plan.Indexing.Count(d => !_workspace.Indexing.IsQueued(d.DocumentId));
        if (elsewhere > 0)
        {
            ScopeNotice = string.Create(System.Globalization.CultureInfo.InvariantCulture,
                $"Khi bạn hỏi, {elsewhere} tài liệu đang được lập chỉ mục ở nơi khác nên câu trả lời này chưa gồm tài liệu đó.");
        }

        if (plan.MissingEmbeddingKeyProvider is { } provider)
        {
            AppendScopeNotice($"{AiErrorMessages.NotConfigured(provider)} {PageOnlyNotice}");
            return null;
        }

        WorkspaceRagResult result = await _ragContext.BuildWorkspaceContextAsync(plan, question, ct: ct);
        if (result.Context is null)
        {
            AppendScopeNotice(PageOnlyNotice);
            return null;
        }

        return new BuiltContext(result.Context, result.Sources);
    }

    private async Task StreamAnswerAsync(ChatMessage aiMessage, string question, string context, CancellationToken ct)
    {
        try
        {
            await foreach (var token in _chatService.AskStreamingAsync(question, context, ct))
            {
                aiMessage.Content += token;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
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

    public void Dispose()
    {
        CancelPendingRequest();
        _workspace.Indexing.Dispose();
        _chatService.Dispose();
    }
}
