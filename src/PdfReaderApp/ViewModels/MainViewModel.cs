using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int ContextPageWindow = 2;

    private readonly IPdfDocumentService _documentService;
    private readonly ISettingsService _settingsService;
    private readonly PdfStructureAnalyzer _analyzer;
    private readonly AiChatService _chatService;

    // SP2 Task 8: index, indexing service, RAG context
    private readonly IDocumentIndex _documentIndex;
    private readonly DocumentIndexingService _indexingService;
    private readonly RagContextService _ragContext;

    // Workspace
    private readonly IWorkspaceStore _workspaceStore;
    private string? _activeWorkspaceId;

    private List<TextBlock> _documentBlocks = new();
    public IReadOnlyList<TextBlock> DocumentBlocks => _documentBlocks;

    // Exposed so PdfViewerControl can ask iText for exact keyword match rectangles to highlight.
    public IPdfDocumentService PdfService => _documentService;
    private bool _isSending;

    private List<PageText> _pageTexts = new();

    private string? _documentId;
    private CancellationTokenSource? _indexCts;

    [ObservableProperty]
    private string _indexingStatusText = string.Empty;

    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private string _selectedSearchQuery = string.Empty;

    [ObservableProperty]
    private string _executedSearchQuery = string.Empty;

    // Mỗi khi người dùng sửa/xóa ô tìm: tắt highlight trên trang ngay lập tức.
    // Nếu ô rỗng: xóa luôn danh sách kết quả và query đã chạy.
    partial void OnSearchQueryChanged(string value)
    {
        SelectedSearchQuery = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            ExecutedSearchQuery = string.Empty;
        }
    }

    [ObservableProperty]
    private string windowTitle = "Ultimate PDF Reader & Editor";

    [ObservableProperty]
    private string? filePath;

    [ObservableProperty]
    private int _currentPage = 1;

    [ObservableProperty]
    private int _totalPages = 1;

    [ObservableProperty]
    private double _zoomLevel = 1.0;

    [ObservableProperty]
    private PdfViewMode _viewMode = PdfViewMode.Continuous;

    [ObservableProperty]
    private bool _showCoverSeparately = true;

    [ObservableProperty]
    private bool _showLibrary = true;

    private const double DefaultChatWidthPx = 350;
    private const double MinChatWidthPx = 280;
    private double _savedChatWidthPx = DefaultChatWidthPx;

    // Bề rộng cột chat (bind hai chiều tới ColumnDefinition.Width trong XAML). Mặc định 0 vì
    // app mở ở thư viện -> panel ẩn. MinWidth cũng động: 0 khi ẩn, 280 khi hiện (nếu để cố định
    // 280 thì không thu cột về 0 được khi ẩn).
    [ObservableProperty]
    private System.Windows.GridLength _chatColumnWidth = new System.Windows.GridLength(0);

    [ObservableProperty]
    private double _chatColumnMinWidth = 0;

    // Library và Workspaces loại trừ nhau; panel chat ẩn khi đang ở một trong hai (lưới phủ vùng chính).
    partial void OnShowLibraryChanged(bool value)
    {
        if (value) ShowWorkspaces = false;
        UpdateChatColumnVisibility();
    }

    // Thu cột chat về 0 khi ở Library/Workspaces; khôi phục bề rộng đã lưu khi đang đọc tài liệu.
    private void UpdateChatColumnVisibility()
    {
        if (ShowLibrary || ShowWorkspaces)
        {
            if (ChatColumnWidth.IsAbsolute && ChatColumnWidth.Value > 0)
                _savedChatWidthPx = ChatColumnWidth.Value;
            ChatColumnWidth = new System.Windows.GridLength(0);
            ChatColumnMinWidth = 0;
        }
        else
        {
            ChatColumnMinWidth = MinChatWidthPx;
            ChatColumnWidth = new System.Windows.GridLength(_savedChatWidthPx);
        }
        OnPropertyChanged(nameof(IsReadingDocument));
    }

    // Đang đọc tài liệu (không ở Thư viện cũng không ở Workspaces) -> hiện các nút đọc trên toolbar.
    public bool IsReadingDocument => !ShowLibrary && !ShowWorkspaces;

    [ObservableProperty]
    private bool _showWorkspaces;

    // S2: workspace đang xem chi tiết
    [ObservableProperty]
    private Workspace? _selectedWorkspace;

    // S2: hiện panel chi tiết workspace (ẩn lưới workspace)
    [ObservableProperty]
    private bool _showWorkspaceDetail;

    // S2: tài liệu trong workspace đang mở chi tiết
    public ObservableCollection<LibraryItem> WorkspaceDocuments { get; } = new();

    // S2: expose activeWorkspaceId cho test
    public string? ActiveWorkspaceId => _activeWorkspaceId;

    // Lưới Workspaces chỉ hiện khi ở vùng Workspaces VÀ chưa mở chi tiết
    public bool ShowWorkspacesGrid => ShowWorkspaces && !ShowWorkspaceDetail;

    // Cập nhật ShowWorkspacesGrid khi ShowWorkspaces thay đổi
    partial void OnShowWorkspacesChanged(bool value)
    {
        if (value) ShowLibrary = false;
        UpdateChatColumnVisibility();
        OnPropertyChanged(nameof(ShowWorkspacesGrid));
    }

    // Cập nhật ShowWorkspacesGrid khi ShowWorkspaceDetail thay đổi
    partial void OnShowWorkspaceDetailChanged(bool value)
    {
        OnPropertyChanged(nameof(ShowWorkspacesGrid));
    }

    // Thông báo lỗi khi tạo workspace (vd tên rỗng); rỗng = không có lỗi.
    [ObservableProperty]
    private string _workspaceNameError = string.Empty;

    public ObservableCollection<Workspace> Workspaces { get; } = new();

    public ObservableCollection<LibraryItem> Library { get; } = new();

    private readonly LibraryService _library;
    private readonly IChatHistoryStore _chatHistory;

    // Đặt chế độ xem (radio-style): bấm nút luôn set mode, không bao giờ bỏ chọn mode hiện tại.
    [RelayCommand]
    private void SetViewMode(string mode)
    {
        if (Enum.TryParse<PdfViewMode>(mode, out var m)) ViewMode = m;
    }

    [ObservableProperty]
    private string _chatInput = string.Empty;

    public ObservableCollection<ChatMessage> ChatMessages { get; } = new();

    public NotesViewModel Notes { get; }
    // Lazy: SnackbarMessageQueue dùng Dispatcher nên khởi tạo khi truy cập lần đầu (tránh lỗi trong unit test).
    private MaterialDesignThemes.Wpf.SnackbarMessageQueue? _notesSnackbar;
    public MaterialDesignThemes.Wpf.SnackbarMessageQueue NotesSnackbar
        => _notesSnackbar ??= new MaterialDesignThemes.Wpf.SnackbarMessageQueue();

    private static string AppDir()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfReaderApp");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }

    private static string IndexDbPath()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfReaderApp");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "index.db");
    }

    public MainViewModel()
        : this(new ITextPdfDocumentService(),
               new WindowsSettingsService(),
               new OpenAiChatClientFactory(),
               new SqliteDocumentIndex(IndexDbPath(),
                   System.IO.Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
               new OpenAiEmbeddingGeneratorFactory())
    { }

    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory,
        IDocumentIndex documentIndex,
        IEmbeddingGeneratorFactory embeddingFactory,
        IChatHistoryStore? chatHistory = null,
        INoteStore? noteStore = null,
        IWorkspaceStore? workspaceStore = null)
    {
        _documentService = documentService;
        _settingsService = settingsService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        _chatService = new AiChatService(settingsService, chatClientFactory);

        _documentIndex = documentIndex;
        _documentIndex.EnsureSchema();
        _indexingService = new DocumentIndexingService(_documentIndex, embeddingFactory, settingsService);
        _ragContext = new RagContextService(_documentIndex, embeddingFactory, settingsService);

        var libraryStore = new SqliteLibraryStore(System.IO.Path.Combine(AppDir(), "library.db"));
        libraryStore.EnsureSchema();
        _library = new LibraryService(libraryStore,
            System.IO.Path.Combine(AppDir(), "library"),
            System.IO.Path.Combine(AppDir(), "library", "thumbs"),
            new PdfReaderApp.Core.RenderEngine());
        ReloadLibrary();

        _chatHistory = chatHistory ?? new SqliteChatHistoryStore(System.IO.Path.Combine(AppDir(), "chats.db"));
        _chatHistory.EnsureSchema();

        var notes = noteStore ?? new SqliteNoteStore(System.IO.Path.Combine(AppDir(), "notes.db"));
        notes.EnsureSchema();
        Notes = new NotesViewModel(notes,
            () => _documentId is null ? (int?)null : CurrentPage - 1,
            idx => CurrentPage = idx + 1,
            () => _documentId);

        // Workspace store: real db in AppDir hoac inject (test)
        _workspaceStore = workspaceStore ?? new SqliteWorkspaceStore(System.IO.Path.Combine(AppDir(), "workspaces.db"));
        _workspaceStore.EnsureSchema();

        // Di trú: chuyển ghi chú cũ (owner_key=documentId) sang default workspace.
        // Bọc try/catch để lỗi store không chặn app khởi động (di trú best-effort, idempotent nên chạy lại được).
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var docs = Library.Select(i => (i.DocumentId, i.Title)).ToList();
        try { Core.WorkspaceMigration.Run(_workspaceStore, notes, docs, now); }
        catch { /* di trú lỗi: app vẫn mở; thử lại lần khởi động sau */ }

        ReloadWorkspaces();
        LoadChatHistory();
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var item = _library.Import(dialog.FileName, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ReloadLibrary();
            OpenLibraryItem(item);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Không thể import file PDF: {ex.Message}", "Lỗi",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ShowLibraryView()
    {
        ReloadLibrary();
        ShowWorkspaces = false;
        ShowLibrary = true;
    }

    public void ReloadWorkspaces()
    {
        Workspaces.Clear();
        foreach (var ws in _workspaceStore.GetAll(includeDefault: false))
            Workspaces.Add(ws);
    }

    [RelayCommand]
    private void ShowWorkspacesView()
    {
        // S2: luôn về lưới khi bấm Workspaces từ navigation rail
        ShowWorkspaceDetail = false;
        ReloadWorkspaces();
        ShowLibrary = false;
        ShowWorkspaces = true;
    }

    [RelayCommand]
    private void CreateWorkspace(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            WorkspaceNameError = "Tên workspace không được để trống.";
            return;
        }
        WorkspaceNameError = string.Empty;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var ws = new Workspace(Guid.NewGuid().ToString("N"), name.Trim(), false, null, now, now);
        _workspaceStore.Upsert(ws);
        ReloadWorkspaces();
    }

    [RelayCommand]
    private void OpenWorkspace(Workspace? workspace)
    {
        if (workspace is null) return;
        SelectedWorkspace = workspace;
        _activeWorkspaceId = workspace.Id;
        Notes.LoadFor(_activeWorkspaceId);
        ReloadWorkspaceDocuments();
        // Giữ ShowWorkspaces = true để IsReadingDocument vẫn false -> không hiện toolbar đọc
        ShowWorkspaces = true;
        ShowWorkspaceDetail = true;
    }

    // S2: nạp lại danh sách tài liệu trong workspace đang xem chi tiết
    private void ReloadWorkspaceDocuments()
    {
        WorkspaceDocuments.Clear();
        if (SelectedWorkspace is null) return;
        var ids = _workspaceStore.GetDocumentIds(SelectedWorkspace.Id);
        foreach (var id in ids)
        {
            var item = Library.FirstOrDefault(i => i.DocumentId == id);
            if (item is not null) WorkspaceDocuments.Add(item);
        }
    }

    // S2: thêm nhiều tài liệu từ thư viện vào workspace (đa chọn)
    [RelayCommand]
    private void AddDocumentsToWorkspace(System.Collections.IList? items)
    {
        if (SelectedWorkspace is null || items is null) return;
        foreach (var obj in items)
        {
            if (obj is LibraryItem it)
                _workspaceStore.AddDocument(SelectedWorkspace.Id, it.DocumentId);
        }
        ReloadWorkspaceDocuments();
    }

    // S2: xoá tài liệu khỏi workspace
    [RelayCommand]
    private void RemoveDocumentFromWorkspace(LibraryItem? item)
    {
        if (item is null || SelectedWorkspace is null) return;
        _workspaceStore.RemoveDocument(SelectedWorkspace.Id, item.DocumentId);
        ReloadWorkspaceDocuments();
    }

    // S2: quay lại lưới workspace từ màn chi tiết
    [RelayCommand]
    private void BackToWorkspaceList()
    {
        ShowWorkspaceDetail = false;
        ReloadWorkspaces();
    }

    // S2: mở tài liệu trong ngữ cảnh workspace (active scope = workspace, không phải default)
    [RelayCommand]
    private void OpenWorkspaceDocument(LibraryItem? item)
    {
        if (item is null || SelectedWorkspace is null) return;
        LoadActiveDocument(item.StoredPath, SelectedWorkspace.Id);
        if (_documentId != null)
        {
            try { _library.MarkOpened(item.DocumentId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); } catch { }
            ShowWorkspaceDetail = false;
            ShowWorkspaces = false;
        }
    }

    [RelayCommand]
    private void OpenLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        LoadActiveDocument(item.StoredPath);
        if (_documentId != null)
        {
            _library.MarkOpened(item.DocumentId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ShowLibrary = false;
        }
    }

    [RelayCommand]
    private void RemoveLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        _library.Remove(item);
        try { _chatHistory.DeleteForDocument(item.DocumentId); } catch { }
        Library.Remove(item);
    }

    private void ReloadLibrary()
    {
        Library.Clear();
        foreach (var i in _library.GetAll()) Library.Add(i);
    }

    // Nạp tài liệu đang hoạt động từ đường dẫn (đã copy trong thư viện). Tái dùng cho cả OpenFile lẫn mở từ thư viện.
    // workspaceScopeId: truyền workspace cụ thể (S2 - mở trong workspace); null = dùng default workspace như cũ.
    private void LoadActiveDocument(string path, string? workspaceScopeId = null)
    {
        FilePath = path;
        try
        {
            _documentService.LoadFile(path);
            _documentBlocks = _analyzer.AnalyzeRich();
            OnPropertyChanged(nameof(DocumentBlocks));
            _pageTexts = _documentService.ExtractPageTexts();

            _documentId = DocumentId.FromFile(path);
            LoadChatHistory();
            // S2: dùng seam ResolveWorkspaceScope để quyết định scope (workspace cụ thể hoặc default)
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string title = System.IO.Path.GetFileNameWithoutExtension(path);
            _activeWorkspaceId = ResolveWorkspaceScope(workspaceScopeId, _documentId, title, nowMs);
            Notes.LoadFor(_activeWorkspaceId);
            OnPropertyChanged(nameof(ActiveWorkspaceId));
            SearchResults.Clear();
            StartBackgroundIndexing();
        }
        catch (Exception ex)
        {
            _documentBlocks = new List<TextBlock>();
            OnPropertyChanged(nameof(DocumentBlocks));
            _documentId = null;
            _activeWorkspaceId = null;
            LoadChatHistory();
            Notes.LoadFor(null);
            System.Windows.MessageBox.Show($"Không thể mở file PDF: {ex.Message}", "Lỗi mở file",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            FilePath = null;
        }
    }

    // S2: seam quyết định workspace scope khi mở tài liệu.
    // Nếu explicitWorkspaceId != null: dùng ngay (mở trong workspace). Ngược lại: lấy/tạo default.
    internal string ResolveWorkspaceScope(string? explicitWorkspaceId, string documentId, string title, long nowUnixMs)
        => explicitWorkspaceId ?? _workspaceStore.GetOrCreateDefaultForDocument(documentId, title, nowUnixMs).Id;

    // Nạp lại khung chat theo sách đang mở: hiện bong bóng cũ và dựng lại bộ nhớ LLM.
    // Sách chưa có lịch sử (hoặc chưa mở sách nào) -> hiện 1 bong bóng chào, reset LLM.
    private void LoadChatHistory()
    {
        ChatMessages.Clear();

        if (_documentId is null)
        {
            ShowGreeting();
            _chatService.ResetConversation();
            return;
        }

        System.Collections.Generic.IReadOnlyList<ChatHistoryEntry> entries;
        try { entries = _chatHistory.GetAll(_documentId); }
        catch { entries = System.Array.Empty<ChatHistoryEntry>(); }

        if (entries.Count == 0)
        {
            ShowGreeting();
            _chatService.ResetConversation();
            return;
        }

        // Hiển thị TẤT CẢ bong bóng để transcript trung thực, nhưng chỉ nạp các lượt
        // "dùng được" làm trí nhớ LLM (bỏ AI rỗng / báo lỗi / bị gián đoạn).
        foreach (var e in entries)
            ChatMessages.Add(new ChatMessage { Role = e.Role, Content = e.Content });
        _chatService.SeedHistory(MemoryTurns(entries.Select(e => (e.Role, e.Content))));
    }

    private void ShowGreeting() => ChatMessages.Add(new ChatMessage
    {
        Role = "AI",
        Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
    });

    // Lọc các lượt để nạp làm trí nhớ LLM. Giữ mọi lượt User; bỏ lượt AI rỗng, là thông báo
    // lỗi (suy ra từ MapError + các hằng số dưới), hoặc chứa sentinel gián đoạn. Bong bóng UI
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

    // Các nội dung AI KHÔNG phải câu trả lời thật (thông báo lỗi). Suy ra từ chính MapError nên
    // tự cập nhật nếu đổi lời lỗi, cộng 2 hằng số dùng ở các nhánh thoát sớm của SendMessage.
    private static readonly System.Collections.Generic.HashSet<string> NonMemoryAiMessages
        = BuildNonMemoryAiMessages();

    private static System.Collections.Generic.HashSet<string> BuildNonMemoryAiMessages()
    {
        var set = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal)
        {
            NotConfiguredMessage,
            UnknownErrorMessage,
        };
        foreach (AiChatError e in Enum.GetValues<AiChatError>())
            set.Add(MapError(e));
        return set;
    }

    private const string NotConfiguredMessage =
        "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key.";
    private const string UnknownErrorMessage = "Đã xảy ra lỗi không xác định khi gọi AI.";

    private void StartBackgroundIndexing()
    {
        if (_documentId is null) return;

        _indexCts?.Cancel();
        _indexCts = new CancellationTokenSource();
        var ct = _indexCts.Token;
        string docId = _documentId;
        string? path = FilePath;
        IReadOnlyList<PageText> pageTexts = _pageTexts;

        var progress = new Progress<IndexingProgress>(p =>
            IndexingStatusText = p.Status == "complete"
                ? string.Empty
                : $"Đang lập chỉ mục: {p.Done}/{p.Total}");

        _ = Task.Run(async () =>
        {
            try { await _indexingService.IndexAsync(docId, path, pageTexts, progress, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                string message = AiErrorClassifier.Classify(ex) switch
                {
                    AiChatError.InsufficientQuota => "Lập chỉ mục AI bị bỏ qua: tài khoản OpenAI hết credits/quota (tìm kiếm văn bản vẫn dùng được).",
                    AiChatError.Unauthorized => "Lập chỉ mục AI bị bỏ qua: API key không hợp lệ (tìm kiếm văn bản vẫn dùng được).",
                    AiChatError.Network => "Lập chỉ mục AI bị bỏ qua: không kết nối được (tìm kiếm văn bản vẫn dùng được).",
                    _ => $"Lập chỉ mục lỗi: {ex.Message}"
                };
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    IndexingStatusText = message);
            }
        }, ct);
    }

    [RelayCommand]
    private async Task SendMessage()
    {
        if (string.IsNullOrWhiteSpace(ChatInput)) return;
        if (_isSending) return;
        _isSending = true;

        try
        {
            string question = ChatInput;
            ChatInput = string.Empty;
            ChatMessages.Add(new ChatMessage { Role = "User", Content = question });

            void PersistTurn(string answer)
            {
                if (_documentId is null) return;
                try
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _chatHistory.Append(_documentId, "User", question, now);
                    _chatHistory.Append(_documentId, "AI", answer, now);
                }
                catch { /* không chặn chat khi lưu lỗi */ }
            }

            if (!_chatService.IsConfigured)
            {
                ChatMessages.Add(new ChatMessage { Role = "AI", Content = NotConfiguredMessage });
                PersistTurn(NotConfiguredMessage);
                return;
            }

            string context;
            if (_documentId is not null)
            {
                string? rag = null;
                try { rag = await _ragContext.BuildContextAsync(_documentId, question); }
                catch { rag = null; }
                context = rag ?? DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);
            }
            else
            {
                context = DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);
            }

            var aiMessage = new ChatMessage { Role = "AI", Content = string.Empty };
            ChatMessages.Add(aiMessage);

            try
            {
                await foreach (var token in _chatService.AskStreamingAsync(question, context))
                {
                    aiMessage.Content += token;
                }
            }
            catch (AiChatException ex)
            {
                aiMessage.Content = MapError(ex.Error);
            }
            catch (Exception)
            {
                aiMessage.Content = UnknownErrorMessage;
            }
            PersistTurn(aiMessage.Content);
        }
        finally
        {
            _isSending = false;
        }
    }

    private static string MapError(AiChatError error) => error switch
    {
        AiChatError.Unauthorized => "API key không hợp lệ, vui lòng kiểm tra lại trong Cài đặt.",
        AiChatError.RateLimit => "Đã vượt giới hạn yêu cầu, vui lòng thử lại sau.",
        AiChatError.InsufficientQuota => "Tài khoản OpenAI không đủ credits/quota. Vui lòng nạp credits và kiểm tra Billing tại platform.openai.com.",
        AiChatError.Network => "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.",
        _ => "Đã xảy ra lỗi khi gọi AI."
    };

    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (_documentId is null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        try
        {
            ExecutedSearchQuery = SearchQuery;
            foreach (var hit in _documentIndex.SearchText(_documentId, SearchQuery))
                SearchResults.Add(hit);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Tìm kiếm lỗi: {ex.Message}", "Search",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void SelectSearchResult(SearchResult? result)
    {
        if (result is null) return;
        CurrentPage = result.PageIndex + 1;
        SelectedSearchQuery = SearchQuery;
    }

    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;

    [RelayCommand]
    private void OpenSettings()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ApiKey))
        {
            _settingsService.SaveApiKey(window.ApiKey.Trim());
        }
    }

    [RelayCommand]
    private void NextPage()
    {
        if (CurrentPage < TotalPages) CurrentPage++;
    }

    [RelayCommand]
    private void PreviousPage()
    {
        if (CurrentPage > 1) CurrentPage--;
    }

    [RelayCommand]
    private void FirstPage() => CurrentPage = 1;

    [RelayCommand]
    private void LastPage() => CurrentPage = TotalPages;

    [RelayCommand]
    private void ReindexDocument()
    {
        if (_documentId is null) return;
        try { _documentIndex.DeleteDocument(_documentId); } catch { }
        StartBackgroundIndexing();
    }

    [RelayCommand]
    private void AddNoteFromSelection(PdfReaderApp.Models.NoteSelection? sel)
    {
        if (sel is null) return;
        Notes.BeginNoteFromSelection(sel.Quote, sel.PageIndex, sel.Rects);
    }

    // One-click lưu câu trả lời AI thành note (không chuyển tab); báo bằng snackbar.
    [RelayCommand]
    private void SaveAnswerAsNote(ChatMessage? msg)
    {
        if (msg is null || msg.Role != "AI" || string.IsNullOrWhiteSpace(msg.Content)) return;
        bool saved = Notes.AddNote(msg.Content, null, null);
        NotesSnackbar.Enqueue(saved ? "Đã lưu vào ghi chú" : "Hãy mở một tài liệu để lưu ghi chú");
    }

    [RelayCommand]
    private void ZoomIn() => ZoomLevel += 0.2;

    [RelayCommand]
    private void ZoomOut()
    {
        if (ZoomLevel > 0.4) ZoomLevel -= 0.2;
    }

    public void Dispose()
    {
        _indexCts?.Cancel();
        _documentIndex.Dispose();
        _documentService.Dispose();
        _chatService.Dispose();
    }
}

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    private string _role = "User";

    [ObservableProperty]
    private string _content = string.Empty;

    public DateTime Timestamp { get; } = DateTime.Now;
}
