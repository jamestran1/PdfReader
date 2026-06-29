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

    // Tab S1: Open Set
    public TabSetViewModel Tabs { get; }

    [ObservableProperty]
    private bool _isWorkspaceSession;

    // SP2 Task 8: index, indexing service, RAG context
    private readonly IDocumentIndex _documentIndex;
    private readonly DocumentIndexingService _indexingService;
    private readonly RagContextService _ragContext;

    // Workspace
    private readonly IWorkspaceStore _workspaceStore;
    private readonly INoteStore _noteStore;
    private string? _activeWorkspaceId;
    // S2: khoá ghi Open Set trong lúc reset/khôi phục để wiring auto-save không ghi đè state đang dựng.
    private bool _isRestoringOpenSet;

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
    private string windowTitle = "Trí Thư";

    [ObservableProperty]
    private string? filePath;

    // Tab S1 (fix #44): trang/zoom/tổng-trang là MỘT nguồn sự thật với tab active.
    // Trong phiên Workspace có tab active: proxy thẳng vào OpenTab (viewer per-tab bind cùng OpenTab)
    // nên toolbar và viewer luôn đồng bộ. Ngoài phiên (đọc lẻ): dùng backing field như cũ.
    private OpenTab? ActiveViewTab => IsWorkspaceSession ? Tabs.ActiveTab : null;

    // Nguồn tài liệu cho viewer ĐỌC LẺ. Trong phiên Workspace: null để viewer đọc-lẻ ngủ yên
    // (không nạp lại, không ghi-ngược zoom/trang qua binding TwoWay vào tab active — bug #44).
    // FilePath vẫn giữ nguyên cho lập chỉ mục/nội bộ.
    public string? StandaloneDocumentSource => IsWorkspaceSession ? null : FilePath;

    partial void OnFilePathChanged(string? value) => OnPropertyChanged(nameof(StandaloneDocumentSource));

    private int _currentPage = 1;
    public int CurrentPage
    {
        get => ActiveViewTab is { } t ? t.Page : _currentPage;
        set
        {
            if (ActiveViewTab is { } t) { t.Page = value; }            // OpenTab.PropertyChanged -> re-raise
            else if (_currentPage != value) { _currentPage = value; OnPropertyChanged(); }
        }
    }

    private int _totalPages = 1;
    public int TotalPages
    {
        get => ActiveViewTab is { } t ? t.TotalPages : _totalPages;
        set
        {
            if (ActiveViewTab is { } t) { t.TotalPages = value; }
            else if (_totalPages != value) { _totalPages = value; OnPropertyChanged(); }
        }
    }

    private double _zoomLevel = 1.0;
    public double ZoomLevel
    {
        get => ActiveViewTab is { } t ? t.Zoom : _zoomLevel;
        set
        {
            if (ActiveViewTab is { } t) { t.Zoom = value; }
            else if (_zoomLevel != value) { _zoomLevel = value; OnPropertyChanged(); }
        }
    }

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

    // #63: bề rộng dải mỏng khi panel phải thu gọn.
    private const double CollapsedStripWidthPx = 48;

    // #63: thu/mở panel phải (chat/notes). Trạng thái GLOBAL -- một cờ cho cả app, không theo tab.
    // Không lưu qua phiên (in-memory); persistence ngoài phạm vi #63.
    [ObservableProperty]
    private bool _isRightPanelCollapsed;

    partial void OnIsRightPanelCollapsedChanged(bool value) => UpdateChatColumnVisibility();

    // Panel mở rộng đầy đủ (Card + splitter) khi đang đọc và chưa thu gọn.
    public bool IsRightPanelExpanded => IsReadingDocument && !IsRightPanelCollapsed;

    // Dải mỏng hiện khi đang đọc và đã thu gọn.
    public bool IsRightPanelStripVisible => IsReadingDocument && IsRightPanelCollapsed;

    [RelayCommand]
    private void ToggleRightPanel() => IsRightPanelCollapsed = !IsRightPanelCollapsed;

    // Library và Workspaces loại trừ nhau; panel chat ẩn khi đang ở một trong hai (lưới phủ vùng chính).
    partial void OnShowLibraryChanged(bool value)
    {
        if (value) ShowWorkspaces = false;
        UpdateChatColumnVisibility();
    }

    // Thu cột chat về 0 khi ở Library/Workspaces; về dải mỏng khi thu gọn; khôi phục bề rộng đã lưu khi mở.
    private void UpdateChatColumnVisibility()
    {
        // Nhớ bề rộng khi panel đang mở rộng thật (>= min) để khôi phục khi mở lại.
        if (ChatColumnWidth.IsAbsolute && ChatColumnWidth.Value >= MinChatWidthPx)
            _savedChatWidthPx = ChatColumnWidth.Value;

        if (ShowLibrary || ShowWorkspaces)
        {
            ChatColumnWidth = new System.Windows.GridLength(0);
            ChatColumnMinWidth = 0;
        }
        else if (IsRightPanelCollapsed)
        {
            ChatColumnWidth = new System.Windows.GridLength(CollapsedStripWidthPx);
            ChatColumnMinWidth = 0;
        }
        else
        {
            ChatColumnMinWidth = MinChatWidthPx;
            ChatColumnWidth = new System.Windows.GridLength(_savedChatWidthPx);
        }
        OnPropertyChanged(nameof(IsReadingDocument));
        OnPropertyChanged(nameof(ActiveNavDestination));
        OnPropertyChanged(nameof(IsRightPanelExpanded));
        OnPropertyChanged(nameof(IsRightPanelStripVisible));
        OnPropertyChanged(nameof(IsDocumentTabStripVisible));
    }

    // Đang đọc tài liệu (không ở Thư viện cũng không ở Workspaces) -> hiện các nút đọc trên toolbar.
    public bool IsReadingDocument => !ShowLibrary && !ShowWorkspaces;

    // #64: Tab Strip tài liệu chỉ hiện khi đang đọc trong phiên workspace (ẩn ở Thư viện/Workspaces).
    public bool IsDocumentTabStripVisible => IsWorkspaceSession && IsReadingDocument;

    // Đích điều hướng đang active trên nav rail; suy ra từ trạng thái màn (Library/Workspaces loại trừ nhau).
    public Core.NavDestination ActiveNavDestination =>
        ShowLibrary ? Core.NavDestination.Library
        : ShowWorkspaces ? Core.NavDestination.Workspaces
        : Core.NavDestination.Reader;

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

    // S3: expose documentId hiện tại để bind vào PdfViewerControl.CurrentDocumentId (lọc highlight)
    public string? CurrentDocumentId => _documentId;

    // #38: có tài liệu đang mở hay không -> bật nút quay lại trình đọc trên nav rail.
    public bool HasDocument => _documentId != null;

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

    // Thông báo lỗi khi tạo/đổi tên workspace; rỗng = không có lỗi.
    [ObservableProperty]
    private string _workspaceNameError = string.Empty;

    // S4: nội dung ô đổi tên workspace trong màn chi tiết.
    [ObservableProperty]
    private string _renameDraft = string.Empty;

    public ObservableCollection<WorkspaceCard> Workspaces { get; } = new();

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
        Tabs = new TabSetViewModel();
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

        _noteStore = noteStore ?? new SqliteNoteStore(System.IO.Path.Combine(AppDir(), "notes.db"));
        _noteStore.EnsureSchema();
        Notes = new NotesViewModel(_noteStore,
            () => _documentId is null ? (int?)null : CurrentPage - 1,
            idx => CurrentPage = idx + 1,
            () => _documentId,
            OpenDocumentForNote);

        // Workspace store: real db in AppDir hoac inject (test)
        _workspaceStore = workspaceStore ?? new SqliteWorkspaceStore(System.IO.Path.Combine(AppDir(), "workspaces.db"));
        _workspaceStore.EnsureSchema();

        // Di trú: chuyển ghi chú cũ (owner_key=documentId) sang default workspace.
        // Bọc try/catch để lỗi store không chặn app khởi động (di trú best-effort, idempotent nên chạy lại được).
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var docs = Library.Select(i => (i.DocumentId, i.Title)).ToList();
        try { Core.WorkspaceMigration.Run(_workspaceStore, _noteStore, docs, now); }
        catch { /* di trú lỗi: app vẫn mở; thử lại lần khởi động sau */ }

        ReloadWorkspaces();
        LoadChatHistory();

        // Tab S1: khi ActiveTab thay đổi -> lưu view-state của tab cũ, hydrate tab mới.
        Tabs.ActiveTabChanged += OnActiveTabChanged;

        // Tab S2: lập lịch lưu Open Set khi danh sách tab thay đổi hoặc tab active đổi.
        Tabs.Tabs.CollectionChanged += (_, _) => ScheduleSaveOpenSet();
        Tabs.ActiveTabChanged += _ => ScheduleSaveOpenSet();
    }

    // OpenTab đang được theo dõi PropertyChanged để re-raise toolbar bindings.
    private OpenTab? _subscribedTab;

    private void OnActiveTabChanged(OpenTab? incoming)
    {
        // View-state sống trên OpenTab (một nguồn sự thật). Theo dõi tab active để khi viewer
        // ghi Page/Zoom/TotalPages thì toolbar (bind vm.CurrentPage/ZoomLevel/TotalPages) cập nhật.
        if (_subscribedTab is not null)
            _subscribedTab.PropertyChanged -= OnActiveTabViewStateChanged;
        _subscribedTab = incoming;
        if (incoming is not null)
            incoming.PropertyChanged += OnActiveTabViewStateChanged;

        if (incoming is not null)
            HydrateTab(incoming);

        // Tab active đổi -> toolbar phải đọc lại view-state của tab mới.
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(TotalPages));
    }

    private void OnActiveTabViewStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OpenTab.Page): OnPropertyChanged(nameof(CurrentPage)); break;
            case nameof(OpenTab.Zoom): OnPropertyChanged(nameof(ZoomLevel)); break;
            case nameof(OpenTab.TotalPages): OnPropertyChanged(nameof(TotalPages)); break;
        }
        ScheduleSaveOpenSet();
    }

    // S2: thu thập Open Set hiện tại và lưu (thay thế) cho workspace đang hoạt động.
    internal void SaveOpenSetNow()
    {
        if (_activeWorkspaceId is null || !IsWorkspaceSession) return;
        long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var activeTab = Tabs.ActiveTab;
        var states = new List<OpenTabState>(Tabs.Tabs.Count);
        for (int order = 0; order < Tabs.Tabs.Count; order++)
        {
            var tab = Tabs.Tabs[order];
            states.Add(new OpenTabState(
                tab.DocumentId, order, ReferenceEquals(tab, activeTab),
                tab.Page, tab.Zoom, tab.ScrollNorm, nowUnixMs));
        }
        try { _workspaceStore.SaveOpenTabs(_activeWorkspaceId, states); }
        catch { /* lưu best-effort: lỗi store không được làm gãy phiên đọc */ }
    }

    private System.Windows.Threading.DispatcherTimer? _saveOpenSetDebounce;

    // Gộp nhiều thay đổi liên tiếp (cuộn/zoom) thành một lần ghi. Môi trường không có Dispatcher
    // (test/headless) -> lưu ngay để hành vi tất định.
    private void ScheduleSaveOpenSet()
    {
        if (_isRestoringOpenSet) return;
        if (_activeWorkspaceId is null || !IsWorkspaceSession) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null) { SaveOpenSetNow(); return; }
        _saveOpenSetDebounce ??= new System.Windows.Threading.DispatcherTimer(
            TimeSpan.FromMilliseconds(400), System.Windows.Threading.DispatcherPriority.Background,
            (_, _) => { _saveOpenSetDebounce!.Stop(); SaveOpenSetNow(); }, dispatcher);
        _saveOpenSetDebounce.Stop();
        _saveOpenSetDebounce.Start();
    }

    // Đổi giữa chế độ workspace (proxy theo tab) và đọc lẻ (backing field) -> toolbar đọc lại nguồn.
    partial void OnIsWorkspaceSessionChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(StandaloneDocumentSource));
        OnPropertyChanged(nameof(IsDocumentTabStripVisible));
    }

    /// <summary>
    /// Seam có thể ghi đè trong test: nạp tài liệu cho tab được kích hoạt.
    /// Cài đặt mặc định gọi LoadActiveDocument (nạp PDFium thật).
    /// Test subclass override thành no-op để không nạp PDF thật.
    /// </summary>
    protected virtual void HydrateTab(OpenTab tab)
    {
        LoadActiveDocument(tab.Path, _activeWorkspaceId, tab.Page);
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
        foreach (var workspace in _workspaceStore.GetAll(includeDefault: false))
            Workspaces.Add(BuildWorkspaceCard(workspace));
    }

    // Dựng card: số tài liệu (label), tiêu đề tài liệu đang đọc (tab active đã lưu), cờ rỗng.
    private WorkspaceCard BuildWorkspaceCard(Workspace workspace)
    {
        int documentCount = _workspaceStore.GetDocumentIds(workspace.Id).Count;
        var activeTab = _workspaceStore.GetOpenTabs(workspace.Id).FirstOrDefault(tab => tab.IsActive);
        string? activeTitle = activeTab is null
            ? null
            : Library.FirstOrDefault(item => item.DocumentId == activeTab.DocumentId)?.Title;
        return new WorkspaceCard(workspace, $"{documentCount} tài liệu", activeTitle, documentCount == 0);
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

    // #38: quay lại trình đọc tài liệu đang mở (thoát Thư viện/Workspaces).
    [RelayCommand]
    private void ShowReaderView()
    {
        ShowWorkspaceDetail = false;
        ShowLibrary = false;
        ShowWorkspaces = false;
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
        Notes.LoadFor(workspace.Id);
        ReloadWorkspaceDocuments();
        ShowWorkspaces = true;
        ShowWorkspaceDetail = true;   // mặc định màn quản lý tài liệu; vào phiên đọc sẽ tắt qua EnterReadingSession
        RestoreOrSeedOpenSet(workspace.Id);   // S2: vào Workspace = vào thẳng phiên đọc (nếu có tài liệu)
    }

    // S2: vào Workspace -> khôi phục lười Open Set đã lưu (chỉ hydrate tab active), hoặc seed một tab khi chưa có state.
    private void RestoreOrSeedOpenSet(string workspaceId)
    {
        // Lưu Open Set của workspace đang rời (nếu có) trước khi reset, để quay lại còn nguyên phiên đọc.
        if (IsWorkspaceSession && _activeWorkspaceId is not null) SaveOpenSetNow();

        // Đọc state đã lưu TRƯỚC khi reset (tránh wiring auto-save ghi đè khi xoá Open Set cũ).
        var savedTabs = _workspaceStore.GetOpenTabs(workspaceId);

        _isRestoringOpenSet = true;
        try
        {
            Tabs.Reset();   // bắt đầu từ Open Set rỗng -> không tích lũy / không trùng tab (#bug 2,3)
            _activeWorkspaceId = workspaceId;
            IsWorkspaceSession = true;

            if (savedTabs.Count > 0)
            {
                var restored = new List<OpenTab>(savedTabs.Count);
                string? activeDocumentId = null;
                foreach (var state in savedTabs)
                {
                    var item = Library.FirstOrDefault(i => i.DocumentId == state.DocumentId);
                    if (item is null) continue;   // tài liệu đã bị gỡ khỏi thư viện -> bỏ qua
                    restored.Add(new OpenTab(state.DocumentId, item.Title, item.StoredPath)
                    {
                        Page = state.Page, Zoom = state.Zoom, ScrollNorm = state.ScrollNorm
                    });
                    if (state.IsActive) activeDocumentId = state.DocumentId;
                }
                if (restored.Count > 0)
                {
                    Tabs.RestoreTabs(restored, activeDocumentId ?? restored[0].DocumentId);
                    EnterReadingSession();
                    return;
                }
            }

            var seedDocumentId = ResolveSeedDocument(workspaceId);
            if (seedDocumentId is null) { IsWorkspaceSession = false; return; }   // workspace rỗng -> giữ màn quản lý
            var seedItem = Library.FirstOrDefault(i => i.DocumentId == seedDocumentId);
            if (seedItem is null) { IsWorkspaceSession = false; return; }
            Tabs.OpenOrActivate(seedItem.DocumentId, seedItem.Title, seedItem.StoredPath);
            EnterReadingSession();
        }
        finally
        {
            _isRestoringOpenSet = false;
            SaveOpenSetNow();   // ghi lại Open Set mới (đặc biệt khi seed tạo tab mới)
        }
    }

    // S2: tài liệu seed khi Workspace chưa có Open Set: ưu tiên DefaultDocumentId, rồi thành viên đầu.
    internal string? ResolveSeedDocument(string workspaceId)
    {
        var workspace = _workspaceStore.Get(workspaceId);
        if (workspace?.DefaultDocumentId is { Length: > 0 } defaultDocumentId) return defaultDocumentId;
        var memberDocumentIds = _workspaceStore.GetDocumentIds(workspaceId);
        return memberDocumentIds.Count > 0 ? memberDocumentIds[0] : null;
    }

    private void EnterReadingSession()
    {
        ShowWorkspaceDetail = false;
        ShowWorkspaces = false;
    }

    // S2: nạp lại danh sách tài liệu trong workspace đang xem chi tiết
    private void ReloadWorkspaceDocuments()
    {
        WorkspaceDocuments.Clear();
        if (SelectedWorkspace is not null)
        {
            var ids = _workspaceStore.GetDocumentIds(SelectedWorkspace.Id);
            foreach (var id in ids)
            {
                var item = Library.FirstOrDefault(i => i.DocumentId == id);
                if (item is not null) WorkspaceDocuments.Add(item);
            }
        }
        // S3: danh sách tài liệu đổi -> cập nhật ngữ cảnh chip nhãn (gọi tại đây để mọi call-site đều được phủ)
        UpdateNotesDocumentContext();
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

    // S3: cập nhật ngữ cảnh chip nhãn tài liệu cho NotesViewModel
    private void UpdateNotesDocumentContext()
    {
        var titles = new Dictionary<string, string>();
        foreach (var d in WorkspaceDocuments) titles[d.DocumentId] = d.Title;
        Notes.SetDocumentContext(titles, showChips: WorkspaceDocuments.Count > 1);
    }

    // S3 / Tab S1: callback mở tài liệu khác trong cùng workspace khi bấm note cross-doc.
    // Cross-doc jump: activate-or-open tab, luôn điều hướng tới trang đích.
    private void OpenDocumentForNote(string documentId, int? pageIndex)
    {
        var item = Library.FirstOrDefault(i => i.DocumentId == documentId);
        if (item is null) return;
        int targetPage = (pageIndex ?? 0) + 1;
        if (IsWorkspaceSession)
        {
            // Activate-or-open + đặt trang đích TRƯỚC khi nạp (initialPage) -> viewer mở thẳng tại trang.
            Tabs.OpenOrActivate(documentId, item.Title, item.StoredPath, targetPage);
        }
        else
        {
            // Standalone: giữ hành vi cũ.
            LoadActiveDocument(item.StoredPath, _activeWorkspaceId, initialPage: targetPage);
        }
    }

    // S2: quay lại lưới workspace từ màn chi tiết
    [RelayCommand]
    private void BackToWorkspaceList()
    {
        ShowWorkspaceDetail = false;
        ReloadWorkspaces();
    }

    // S4: xóa một workspace (không được xóa default workspace)
    [RelayCommand]
    private void DeleteWorkspace(Workspace? ws)
    {
        if (ws is null) return;
        // Chặn xóa default workspace (default được ẩn khỏi danh sách; đây là phòng vệ)
        if (ws.IsDefault) return;
        _noteStore.DeleteForOwner(ws.Id);
        _workspaceStore.Delete(ws.Id);
        if (_activeWorkspaceId == ws.Id)
        {
            _activeWorkspaceId = null;
            Notes.LoadFor(null);
        }
        if (ShowWorkspaceDetail && SelectedWorkspace?.Id == ws.Id)
        {
            SelectedWorkspace = null; // tránh giữ tham chiếu tới workspace đã xóa
            ShowWorkspaceDetail = false;
        }
        ReloadWorkspaces();
    }

    // S4: đổi tên workspace đang mở chi tiết
    [RelayCommand]
    private void RenameWorkspace(string? newName)
    {
        if (SelectedWorkspace is null) return;
        if (string.IsNullOrWhiteSpace(newName))
        {
            WorkspaceNameError = "Tên workspace không được để trống.";
            return;
        }
        WorkspaceNameError = string.Empty;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        _workspaceStore.Rename(SelectedWorkspace.Id, newName.Trim(), now);
        // Cập nhật header chi tiết workspace
        SelectedWorkspace = _workspaceStore.Get(SelectedWorkspace.Id);
        RenameDraft = string.Empty;
        ReloadWorkspaces();
    }

    // Tab S1: kích hoạt tab được chọn trên Tab Strip.
    [RelayCommand]
    private void ActivateTab(Models.OpenTab? tab)
    {
        if (tab is null) return;
        Tabs.ActivateTab(tab);
    }

    // Tab S1: đóng tab (chỉ gỡ khỏi Open Set, KHÔNG gỡ khỏi workspace membership).
    [RelayCommand]
    private void CloseTab(Models.OpenTab? tab)
    {
        if (tab is null) return;
        Tabs.Close(tab);
    }

    // Tab S3 (#62): nút "+" trên Tab Strip mở Workspace Documents surface để thêm tài liệu.
    // Tạm điều hướng về màn quản lý tài liệu của workspace; surface hai-vùng đầy đủ
    // sẽ thay thế ở #47 (repoint lệnh này), nên #62 không bị chặn bởi #47.
    [RelayCommand]
    private void ShowWorkspaceDocuments()
    {
        if (SelectedWorkspace is null) return;
        IsWorkspaceSession = false;   // ẩn viewer host; Open Set vẫn giữ trong TabSetViewModel
        ShowWorkspaces = true;
        ShowWorkspaceDetail = true;
    }

    // S2 / Tab S1: mở tài liệu trong ngữ cảnh workspace -> route qua Open Set.
    [RelayCommand]
    private void OpenWorkspaceDocument(LibraryItem? item)
    {
        if (item is null || SelectedWorkspace is null) return;
        // Tab S1: đảm bảo workspace scope được thiết lập trước khi HydrateTab chạy.
        _activeWorkspaceId = SelectedWorkspace.Id;
        IsWorkspaceSession = true;
        var tab = Tabs.OpenOrActivate(item.DocumentId, item.Title, item.StoredPath);
        // best-effort: cập nhật thời điểm mở
        try { _library.MarkOpened(item.DocumentId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); } catch { }
        ShowWorkspaceDetail = false;
        ShowWorkspaces = false;
    }

    [RelayCommand]
    private void OpenLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        // Tab S1: standalone open -> không tích lũy tab; giữ hành vi thay thế cũ.
        IsWorkspaceSession = false;
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
        string docId = item.DocumentId;
        // S4: dọn dẹp workspace cascade khi xóa tài liệu khỏi thư viện.
        // Không có FK chéo DB (ADR 0002) -> phối hợp xóa trong code; nếu một bước SQLite lỗi giữa chừng
        // có thể để lại trạng thái lệch một phần (rủi ro chấp nhận được, chạy lại xóa là idempotent).
        foreach (var wsId in _workspaceStore.GetWorkspaceIdsForDocument(docId).ToList())
        {
            var ws = _workspaceStore.Get(wsId);
            if (ws is { IsDefault: true })
            {
                // Xóa default workspace của tài liệu + notes của nó
                _noteStore.DeleteForOwner(wsId);
                _workspaceStore.Delete(wsId);
            }
            else
            {
                // Workspace dùng chung: chỉ gỡ tài liệu ra, GIỮ các note neo tài liệu đó (mục 3)
                _workspaceStore.RemoveDocument(wsId, docId);
            }
        }
        // Dọn note neo trực tiếp tới tài liệu ở mọi nơi
        _noteStore.DeleteForDocument(docId);
        try { _chatHistory.DeleteForDocument(docId); } catch { }
        try { _documentIndex.DeleteDocument(docId); } catch { }
        _library.Remove(item);
        Library.Remove(item);
        ReloadWorkspaces();
    }

    private void ReloadLibrary()
    {
        Library.Clear();
        foreach (var i in _library.GetAll()) Library.Add(i);
    }

    // Nạp tài liệu đang hoạt động từ đường dẫn (đã copy trong thư viện). Tái dùng cho cả OpenFile lẫn mở từ thư viện.
    // workspaceScopeId: truyền workspace cụ thể (S2 - mở trong workspace); null = dùng default workspace như cũ.
    private void LoadActiveDocument(string path, string? workspaceScopeId = null, int initialPage = 1)
    {
        // Đặt trang đích TRƯỚC khi gán FilePath: việc gán FilePath kích hoạt control nạp tài liệu
        // và bố cục ngay theo CurrentPage, nên mở thẳng tại trang neo (tránh nạp-rồi-nhảy bị reset ghi đè).
        CurrentPage = initialPage;
        FilePath = path;
        try
        {
            _documentService.LoadFile(path);
            _documentBlocks = _analyzer.AnalyzeRich();
            OnPropertyChanged(nameof(DocumentBlocks));
            _pageTexts = _documentService.ExtractPageTexts();

            _documentId = DocumentId.FromFile(path);
            OnPropertyChanged(nameof(CurrentDocumentId));
            OnPropertyChanged(nameof(HasDocument));
            LoadChatHistory();
            // S2: dùng seam ResolveWorkspaceScope để quyết định scope (workspace cụ thể hoặc default)
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string title = System.IO.Path.GetFileNameWithoutExtension(path);
            _activeWorkspaceId = ResolveWorkspaceScope(workspaceScopeId, _documentId, title, nowMs);
            Notes.LoadFor(_activeWorkspaceId);
            UpdateNotesDocumentContext();
            OnPropertyChanged(nameof(ActiveWorkspaceId));
            SearchResults.Clear();
            StartBackgroundIndexing();
        }
        catch (Exception ex)
        {
            _documentBlocks = new List<TextBlock>();
            OnPropertyChanged(nameof(DocumentBlocks));
            _documentId = null;
            // S2: nếu mở trong workspace (workspaceScopeId có giá trị), giữ nguyên _activeWorkspaceId
            // để scope workspace không bị xoá khi file tạm thời không đọc được.
            if (workspaceScopeId is null) _activeWorkspaceId = null;
            OnPropertyChanged(nameof(CurrentDocumentId));
            OnPropertyChanged(nameof(HasDocument));
            LoadChatHistory();
            Notes.LoadFor(null);
            UpdateNotesDocumentContext();
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
