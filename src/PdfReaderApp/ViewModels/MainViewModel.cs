using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IPdfDocumentService _documentService;
    private readonly ISettingsService _settingsService;
    private readonly IThemeService _themeService;
    private readonly PdfStructureAnalyzer _analyzer;

    // Tab S1: Open Set
    public TabSetViewModel Tabs { get; }

    [ObservableProperty]
    public partial bool IsWorkspaceSession { get; set; }

    [ObservableProperty]
    public partial bool IsDarkMode { get; set; }

    [RelayCommand]
    private void ToggleTheme()
    {
        AppTheme current = _settingsService.GetThemePreference();
        AppTheme next = current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
        _themeService.Apply(next);
        _settingsService.SaveThemePreference(next);
        IsDarkMode = next == AppTheme.Dark;
    }

    // SP2 Task 8: index, indexing service, RAG context
    private readonly IDocumentIndex _documentIndex;

    // Workspace
    private readonly IWorkspaceStore _workspaceStore;
    private readonly INoteStore _noteStore;
    private string? _activeWorkspaceId;
    private OpenSetPersistence _openSet = null!;   // dựng trong WireTabEvents (cần Tabs và _workspaceStore)

    private IReadOnlyList<TextBlock> _documentBlocks = [];
    public IReadOnlyList<TextBlock> DocumentBlocks => _documentBlocks;

    // Exposed so PdfViewerControl can ask iText for exact keyword match rectangles to highlight.
    public IPdfDocumentService PdfService => _documentService;

    private IReadOnlyList<PageText> _pageTexts = [];

    private string? _documentId;
    public SearchViewModel Search { get; }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Trí Thư";

    [ObservableProperty]
    public partial string? FilePath { get; set; }

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
    public partial PdfViewMode ViewMode { get; set; } = PdfViewMode.Continuous;

    [ObservableProperty]
    public partial bool ShowCoverSeparately { get; set; } = true;

    [ObservableProperty]
    public partial bool ShowLibrary { get; set; } = true;

    private const double DefaultChatWidthPx = 350;
    private const double MinChatWidthPx = 280;
    private double _savedChatWidthPx = DefaultChatWidthPx;

    // Bề rộng cột chat (bind hai chiều tới ColumnDefinition.Width trong XAML). Mặc định 0 vì
    // app mở ở thư viện -> panel ẩn. MinWidth cũng động: 0 khi ẩn, 280 khi hiện (nếu để cố định
    // 280 thì không thu cột về 0 được khi ẩn).
    [ObservableProperty]
    public partial System.Windows.GridLength ChatColumnWidth { get; set; } = new System.Windows.GridLength(0);

    [ObservableProperty]
    public partial double ChatColumnMinWidth { get; set; } = 0;

    // #63: bề rộng dải mỏng khi panel phải thu gọn.
    private const double CollapsedStripWidthPx = 48;

    // #63: thu/mở panel phải (chat/notes). Trạng thái GLOBAL -- một cờ cho cả app, không theo tab.
    // Không lưu qua phiên (in-memory); persistence ngoài phạm vi #63.
    [ObservableProperty]
    public partial bool IsRightPanelCollapsed { get; set; }

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
        OnPropertyChanged(nameof(CanPromoteToWorkspace));
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
    public partial bool ShowWorkspaces { get; set; }

    // S2: workspace đang xem chi tiết
    [ObservableProperty]
    public partial Workspace? SelectedWorkspace { get; set; }

    // S2: expose activeWorkspaceId cho test
    public string? ActiveWorkspaceId => _activeWorkspaceId;

    // S3: expose documentId hiện tại để bind vào PdfViewerControl.CurrentDocumentId (lọc highlight)
    public string? CurrentDocumentId => _documentId;

    // #38: có tài liệu đang mở hay không -> bật nút quay lại trình đọc trên nav rail.
    public bool HasDocument => _documentId != null;

    // Cập nhật chat column khi ShowWorkspaces thay đổi
    partial void OnShowWorkspacesChanged(bool value)
    {
        if (value) ShowLibrary = false;
        UpdateChatColumnVisibility();
    }

    // Thông báo lỗi khi tạo/đổi tên workspace; rỗng = không có lỗi.
    [ObservableProperty]
    public partial string WorkspaceNameError { get; set; } = string.Empty;

    public ObservableCollection<WorkspaceCard> Workspaces { get; } = [];

    public ObservableCollection<LibraryItem> Library { get; } = [];

    private readonly LibraryService _library;

    // Đặt chế độ xem (radio-style): bấm nút luôn set mode, không bao giờ bỏ chọn mode hiện tại.
    [RelayCommand]
    private void SetViewMode(string mode)
    {
        if (Enum.TryParse<PdfViewMode>(mode, out var m)) ViewMode = m;
    }

    public NotesViewModel Notes { get; }

    public IndexingViewModel Indexing { get; }
    public ChatViewModel Chat { get; }
    public WorkspaceDocumentsViewModel DocumentsSurface { get; }
    public NotificationViewModel Notifications { get; } = new();

    public void NotifySuccess(string message) => Notifications.ShowSuccess(message);
    public void NotifyError(string message) => Notifications.ShowError(message);

    public MainViewModel()
        : this(DefaultServices.Create())
    { }

    // Giữ chữ ký nhiều tham số cho test; tham số tuỳ chọn null thì lấy bản mặc định từ DefaultServices.
    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory,
        IDocumentIndex documentIndex,
        IEmbeddingGeneratorFactory embeddingFactory,
        IChatHistoryStore? chatHistory = null,
        INoteStore? noteStore = null,
        IWorkspaceStore? workspaceStore = null,
        IThemeService? themeService = null)
        : this(new MainViewModelServices(
            documentService,
            settingsService,
            chatClientFactory,
            documentIndex,
            embeddingFactory,
            chatHistory ?? DefaultServices.CreateChatHistory(AppDataDirectory.Current()),
            noteStore ?? DefaultServices.CreateNoteStore(AppDataDirectory.Current()),
            workspaceStore ?? DefaultServices.CreateWorkspaceStore(AppDataDirectory.Current()),
            themeService ?? DefaultServices.CreateThemeService(),
            DefaultServices.CreateLibrary(AppDataDirectory.Current())))
    { }

    public MainViewModel(MainViewModelServices services)
    {
        Tabs = new TabSetViewModel();
        _documentService = services.DocumentService;
        _settingsService = services.Settings;
        _themeService = services.ThemeService;
        IsDarkMode = _settingsService.GetThemePreference() == AppTheme.Dark;
        _analyzer = new PdfStructureAnalyzer(_documentService);

        _documentIndex = services.DocumentIndex;
        _documentIndex.EnsureSchema();
        Indexing = new IndexingViewModel(
            _documentIndex,
            new DocumentIndexingService(_documentIndex, services.EmbeddingFactory, _settingsService),
            () => _documentId is null ? null : new IndexingTarget(_documentId, FilePath, _pageTexts));

        _library = services.Library;
        ReloadLibrary();

        Chat = ChatViewModel.Create(services, GetChatOwnerId, GetDocumentSnapshot, GetWorkspaceDocuments, OpenCitationSource);

        _noteStore = services.NoteStore;
        _noteStore.EnsureSchema();
        Notes = new NotesViewModel(_noteStore,
            () => _documentId is null ? (int?)null : CurrentPage - 1,
            idx => CurrentPage = idx + 1,
            () => _documentId,
            OpenDocumentForNote,
            NotifyError);
        Search = new SearchViewModel(
            _documentIndex, () => _documentId, page => CurrentPage = page, NotifyError,
            new SearchWorkspaceContext(GetSearchDocuments, JumpToDocumentFromSearch));

        _workspaceStore = services.WorkspaceStore;
        _workspaceStore.EnsureSchema();

        MigrateLegacyNotesToDefaultWorkspace();

        ReloadWorkspaces();
        Chat.LoadHistory();

        DocumentsSurface = CreateDocumentsSurface();

        WireTabEvents();
    }

    private void MigrateLegacyNotesToDefaultWorkspace()
    {
        // Di trú: chuyển ghi chú cũ (owner_key=documentId) sang default workspace.
        // Bọc try/catch để lỗi store không chặn app khởi động (di trú best-effort, idempotent nên chạy lại được).
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var docs = Library.Select(i => (i.DocumentId, i.Title)).ToList();
        try { Core.WorkspaceMigration.Run(_workspaceStore, _noteStore, docs, now); }
#pragma warning disable CA1031 // degrade: di trú lỗi thì app vẫn mở, thử lại lần khởi động sau
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"MainViewModel: di trú workspace lỗi: {ex}"); /* di trú lỗi: app vẫn mở; thử lại lần khởi động sau */ }
#pragma warning restore CA1031
    }

    private WorkspaceDocumentsViewModel CreateDocumentsSurface()
        => new WorkspaceDocumentsViewModel(
            _workspaceStore,
            activeWorkspace: () => SelectedWorkspace,
            libraryItems: () => Library,
            openTab: item => OpenWorkspaceDocument(item),
            closeTabForDocument: documentId =>
            {
                var openTab = Tabs.Tabs.FirstOrDefault(t => t.DocumentId == documentId);
                if (openTab is not null) Tabs.Close(openTab);
            },
            notify: NotifySuccess,
            stateChanged: OnDocumentsSurfaceStateChanged);

    private void WireTabEvents()
    {
        // Khi ActiveTab thay đổi -> lưu view-state của tab cũ, hydrate tab mới.
        Tabs.ActiveTabChanged += OnActiveTabChanged;

        // Dựng sau khi đăng ký OnActiveTabChanged để thứ tự xử lý ActiveTabChanged không đổi.
        _openSet = new OpenSetPersistence(
            _workspaceStore, Tabs, () => _activeWorkspaceId, () => IsWorkspaceSession);
    }

    // OpenTab đang được theo dõi PropertyChanged để re-raise toolbar bindings.
    private OpenTab? _subscribedTab;

    private void OnActiveTabChanged(OpenTab? incoming)
    {
        // Đổi hoặc đóng tab: câu trả lời đang chạy thuộc tài liệu của tab cũ (kể cả khi đóng tab cuối, không nạp lại chat).
        Chat.CancelPendingRequest();

        // View-state sống trên OpenTab (một nguồn sự thật). Theo dõi tab active để khi viewer
        // ghi Page/Zoom/TotalPages thì toolbar (bind vm.CurrentPage/ZoomLevel/TotalPages) cập nhật.
        if (_subscribedTab is not null)
        {
            _subscribedTab.PropertyChanged -= OnActiveTabViewStateChanged;
            _subscribedTab.NotesScroll = Notes.NotesScroll;
            _subscribedTab.SelectedNoteId = Notes.SelectedNoteId;
            _subscribedTab.FilterCurrentDocumentOnly = Notes.FilterCurrentDocumentOnly;
        }

        _subscribedTab = incoming;
        if (incoming is not null)
        {
            incoming.PropertyChanged += OnActiveTabViewStateChanged;
            Notes.NotesScroll = incoming.NotesScroll;
            Notes.SelectedNoteId = incoming.SelectedNoteId;
            Notes.FilterCurrentDocumentOnly = incoming.FilterCurrentDocumentOnly;
        }

        // Nhảy từ kết quả tìm Toàn Workspace: giữ ô tìm, kết quả và phạm vi để bấm tiếp kết quả khác.
        if (!_keepSearchOnTabChange) Search.Reset();

        // Tab mới (hoặc không còn tab) thì đoạn nguồn của tài liệu trước không còn nghĩa.
        ActiveSourcePassage = null;

        if (incoming is not null)
            HydrateTab(incoming);
        else if (!_openSet.IsRestoring)
            RefreshWorkspaceDocumentsSurface();   // đóng tab cuối -> surface inline cần dữ liệu mới

        // Tab active đổi -> toolbar phải đọc lại view-state của tab mới.
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(TotalPages));
    }

    private void OnDocumentsSurfaceStateChanged()
    {
        if (SelectedWorkspace is not null)
            SelectedWorkspace = _workspaceStore.Get(SelectedWorkspace.Id);
        UpdateNotesDocumentContext();
        ReloadWorkspaces();
    }

    private void RefreshWorkspaceDocumentsSurface()
    {
        DocumentsSurface.Refresh();
        UpdateNotesDocumentContext();
    }

    private void OnActiveTabViewStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OpenTab.Page): OnPropertyChanged(nameof(CurrentPage)); break;
            case nameof(OpenTab.Zoom): OnPropertyChanged(nameof(ZoomLevel)); break;
            case nameof(OpenTab.TotalPages): OnPropertyChanged(nameof(TotalPages)); break;
        }
        _openSet.Schedule();
    }

    internal void SaveOpenSetNow() => _openSet.SaveNow();

    // Đổi giữa chế độ workspace (proxy theo tab) và đọc lẻ (backing field) -> toolbar đọc lại nguồn.
    partial void OnIsWorkspaceSessionChanged(bool value)
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(TotalPages));
        OnPropertyChanged(nameof(StandaloneDocumentSource));
        OnPropertyChanged(nameof(IsDocumentTabStripVisible));
        OnPropertyChanged(nameof(CanPromoteToWorkspace));
    }

    // Seam của phần dọn kết quả tìm khi nạp tài liệu: test gọi lại để đi qua đúng đường nạp thật mà không cần PDF.
    // Nạp tài liệu chạy đồng bộ trong lúc đổi tab nên cờ nhảy-từ-kết-quả-tìm vẫn còn hiệu lực ở đây.
    protected void ClearSearchResultsForLoadedDocument()
    {
        if (!_keepSearchOnTabChange) Search.ClearResults();
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

    /// <summary>
    /// Tài liệu đang đọc lẻ để promote (null nếu không có). Mặc định đọc state thật;
    /// test override để khỏi nạp PDF thật (giống seam HydrateTab).
    /// </summary>
    protected virtual StandaloneDocument? CurrentStandaloneDocument()
    {
        if (_documentId is null) return null;
        var item = Library.FirstOrDefault(libraryItem => libraryItem.DocumentId == _documentId);
        return new StandaloneDocument(
            _documentId,
            item?.Title ?? System.IO.Path.GetFileNameWithoutExtension(FilePath ?? string.Empty),
            item?.StoredPath ?? FilePath ?? string.Empty,
            _currentPage,
            _zoomLevel);
    }

    // #68: chỉ hiện affordance promote khi đang đọc lẻ một tài liệu (Default Workspace).
    public bool CanPromoteToWorkspace => IsReadingDocument && !IsWorkspaceSession && HasDocument;

    // #68: promote Default Workspace -> named Workspace từ tài liệu đang đọc lẻ.
    // Tái dùng tạo workspace + AddDocument + OpenOrActivate; mang theo trang/zoom.
    [RelayCommand]
    private void PromoteToWorkspace(string? name)
    {
        if (IsWorkspaceSession) return;
        if (CurrentStandaloneDocument() is not { } document) return;
        if (string.IsNullOrWhiteSpace(name))
        {
            WorkspaceNameError = "Tên workspace không được để trống.";
            return;
        }
        WorkspaceNameError = string.Empty;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var workspace = new Workspace(Guid.NewGuid().ToString("N"), name.Trim(), false, null, now, now);
        _workspaceStore.Upsert(workspace);
        _workspaceStore.AddDocument(workspace.Id, document.DocumentId);

        SelectedWorkspace = workspace;
        _activeWorkspaceId = workspace.Id;
        IsWorkspaceSession = true;

        var tab = Tabs.OpenOrActivate(document.DocumentId, document.Title, document.Path, initialPage: document.Page);
        tab.Zoom = document.Zoom;

        // Nạp danh sách tài liệu thành viên của workspace mới (giống OpenWorkspace), để surface "+" hiển thị đúng.
        RefreshWorkspaceDocumentsSurface();
        ReloadWorkspaces();
    }

    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        ImportAndOpen(dialog.FileName);
    }

    /// <summary>Import một file PDF vào thư viện rồi mở nó (dùng cho Mở PDF và cho ảnh chụp dev).</summary>
    internal void ImportAndOpen(string path)
    {
        try
        {
            var item = _library.Import(path, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ReloadLibrary();
            OpenLibraryItem(item);
        }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: import lỗi thì báo cho người dùng
        catch (Exception ex)
        {
            NotifyError($"Không thể import file PDF: {ex.Message}");
        }
#pragma warning restore CA1031
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
        ReloadWorkspaces();
        ShowLibrary = false;
        ShowWorkspaces = true;
    }

    // #38: quay lại trình đọc tài liệu đang mở (thoát Thư viện/Workspaces).
    [RelayCommand]
    private void ShowReaderView()
    {
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
        RefreshWorkspaceDocumentsSurface();
        ShowLibrary = false;   // thoát Thư viện khi mở workspace (EnterReadingSession không tắt Library)
        RestoreOrSeedOpenSet(workspace.Id);   // S2: vào Workspace = vào thẳng phiên đọc (nếu có tài liệu)
    }

    // S2: vào Workspace -> khôi phục lười Open Set đã lưu (chỉ hydrate tab active), hoặc seed một tab khi chưa có state.
    private void RestoreOrSeedOpenSet(string workspaceId)
    {
        // Lưu Open Set của workspace đang rời (nếu có) trước khi reset, để quay lại còn nguyên phiên đọc.
        if (IsWorkspaceSession && _activeWorkspaceId is not null) SaveOpenSetNow();

        // Đọc state đã lưu TRƯỚC khi reset (tránh wiring auto-save ghi đè khi xoá Open Set cũ).
        var savedTabs = _workspaceStore.GetOpenTabs(workspaceId);

        _openSet.IsRestoring = true;
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
                        Page = state.Page,
                        Zoom = state.Zoom,
                        ScrollNorm = state.ScrollNorm
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
            // Workspace rỗng / seed không còn trong Library: vẫn vào phiên workspace với Open Set rỗng —
            // canvas hiện Workspace Documents surface inline (grill #47, thay màn quản lý cũ).
            if (seedDocumentId is null) { EnterReadingSession(); return; }
            var seedItem = Library.FirstOrDefault(i => i.DocumentId == seedDocumentId);
            if (seedItem is null) { EnterReadingSession(); return; }
            Tabs.OpenOrActivate(seedItem.DocumentId, seedItem.Title, seedItem.StoredPath);
            EnterReadingSession();
        }
        finally
        {
            _openSet.IsRestoring = false;
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
        ShowWorkspaces = false;
    }

    // S3: cập nhật ngữ cảnh chip nhãn tài liệu cho NotesViewModel
    private void UpdateNotesDocumentContext()
    {
        var titles = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var document in DocumentsSurface.Members) titles[document.DocumentId] = document.Title;
        Notes.SetDocumentContext(titles, showChips: titles.Count > 1);
    }

    // Các Document của Workspace đang mở (kèm tên) để tìm Toàn Workspace; ngoài phiên Workspace chỉ có tài liệu hiện tại.
    private List<SearchDocument> GetSearchDocuments()
        => [.. GetWorkspaceDocuments().Select(d => new SearchDocument(d.DocumentId, d.Title))];

    // Cùng danh sách cho tìm kiếm và hỏi đáp Toàn Workspace; kèm đường dẫn file để lập chỉ mục nền tài liệu chưa mở.
    private List<WorkspaceDocument> GetWorkspaceDocuments()
    {
        if (_activeWorkspaceId is null)
        {
            return _documentId is null
                ? []
                : [new WorkspaceDocument(_documentId, CurrentDocumentTitle(), FilePath ?? string.Empty)];
        }

        var documents = new List<WorkspaceDocument>();
        foreach (string id in _workspaceStore.GetDocumentIds(_activeWorkspaceId))
        {
            var item = Library.FirstOrDefault(i => i.DocumentId == id);
            if (item is not null) documents.Add(new WorkspaceDocument(id, item.Title, item.StoredPath));
        }

        return documents;
    }

    // Tab đổi do chính kết quả tìm kiếm gây ra thì không xoá trạng thái tìm kiếm (xem OnActiveTabChanged).
    private bool _keepSearchOnTabChange;

    private void JumpToDocumentFromSearch(string documentId, int pageIndex)
    {
        _keepSearchOnTabChange = true;
        try { JumpToDocument(documentId, pageIndex); }
        finally { _keepSearchOnTabChange = false; }
    }

    /// <summary>Đoạn nguồn của trích dẫn AI vừa mở, viewer của Document đó tô trên trang; null khi chưa mở trích dẫn
    /// nào hoặc nguồn không mang chữ của đoạn (lịch sử chat bản cũ).</summary>
    [ObservableProperty]
    public partial SourcePassage? ActiveSourcePassage { get; set; }

    /// <summary>Mở nguồn của trích dẫn <c>[n]</c>: Cross-doc jump tới Document và trang được trích rồi tô đoạn nguồn.</summary>
    public void OpenCitationSource(RagSource source, string? quote = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        // Nhảy trước rồi mới đặt đoạn nguồn: đổi tài liệu xoá đoạn nguồn cũ, nên đặt sau thì không bị xoá mất.
        // Tài liệu đã gỡ khỏi Library thì không nhảy được; không giữ đoạn của trích dẫn trước tô nhầm chỗ.
        ActiveSourcePassage = TryOpenDocument(source.DocumentId, source.PageIndex) ? SourcePassage.From(source, quote) : null;
    }

    /// <summary>Tắt tô Đoạn nguồn (phím Esc).</summary>
    [RelayCommand]
    private void ClearSourcePassage() => ActiveSourcePassage = null;

    /// <summary>Cross-doc jump: mở hoặc kích hoạt tab của <paramref name="documentId"/> rồi cuộn tới trang
    /// (<paramref name="pageIndex"/> tính từ 0). Dùng chung cho note, kết quả tìm kiếm và trích dẫn AI.</summary>
    public void JumpToDocument(string documentId, int pageIndex) => OpenDocumentForNote(documentId, pageIndex);

    // S3 / Tab S1: callback mở tài liệu khác trong cùng workspace khi bấm note cross-doc.
    private void OpenDocumentForNote(string documentId, int? pageIndex) => TryOpenDocument(documentId, pageIndex);

    // Cross-doc jump: activate-or-open tab, luôn điều hướng tới trang đích. false khi tài liệu không còn trong Library.
    private bool TryOpenDocument(string documentId, int? pageIndex)
    {
        var item = Library.FirstOrDefault(i => i.DocumentId == documentId);
        if (item is null) return false;
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
        return true;
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
            IsWorkspaceSession = false;
            DocumentsSurface.IsModalOpen = false;
            Notes.LoadFor(null);
            SelectedWorkspace = null;   // null trước Tabs.Reset() — tránh Refresh stale qua OnActiveTabChanged
            Tabs.Reset();
        }
        else if (SelectedWorkspace?.Id == ws.Id)
        {
            SelectedWorkspace = null;
        }

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

    // Tab S3 (#62) / #47: nút "+" mở Workspace Documents surface dạng modal (DialogHost).
    [RelayCommand]
    private void ShowWorkspaceDocuments()
    {
        if (SelectedWorkspace is null) return;
        DocumentsSurface.Refresh();
        DocumentsSurface.IsModalOpen = true;
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
#pragma warning disable CA1031 // best-effort: cập nhật thời điểm mở, lỗi không chặn việc mở tài liệu
        try { _library.MarkOpened(item.DocumentId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"MainViewModel.OpenWorkspaceItem: cập nhật thời điểm mở lỗi: {ex}"); }
#pragma warning restore CA1031
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
                // Bất biến Open Set ⊆ membership (#88): gỡ khỏi Library → gỡ membership → đóng Tab nếu workspace đang active.
                if (wsId == _activeWorkspaceId)
                {
                    var openTab = Tabs.Tabs.FirstOrDefault(t => t.DocumentId == docId);
                    if (openTab is not null) Tabs.Close(openTab);
                }
            }
        }
        // Dọn note neo trực tiếp tới tài liệu ở mọi nơi
        _noteStore.DeleteForDocument(docId);
        Chat.DeleteHistoryFor(docId);
#pragma warning disable CA1031 // best-effort: dọn chỉ mục, lỗi không chặn việc xoá tài liệu
        try { _documentIndex.DeleteDocument(docId); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"MainViewModel.RemoveLibraryItem: xoá chỉ mục lỗi: {ex}"); }
#pragma warning restore CA1031
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
        ActiveSourcePassage = null;   // đoạn nguồn thuộc tài liệu cũ; OpenCitationSource đặt lại sau khi nhảy
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
            OnPropertyChanged(nameof(CanPromoteToWorkspace));
            // S2: dùng seam ResolveWorkspaceScope để quyết định scope (workspace cụ thể hoặc default)
            long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            string title = System.IO.Path.GetFileNameWithoutExtension(path);
            _activeWorkspaceId = ResolveWorkspaceScope(workspaceScopeId, _documentId, title, nowMs);
            Chat.LoadHistory();
            Notes.LoadFor(_activeWorkspaceId);
            UpdateNotesDocumentContext();
            OnPropertyChanged(nameof(ActiveWorkspaceId));
            ClearSearchResultsForLoadedDocument();
            Indexing.StartBackgroundIndexing();
        }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: nạp tài liệu lỗi thì báo cho người dùng
        catch (Exception ex)
        {
            _documentBlocks = [];
            OnPropertyChanged(nameof(DocumentBlocks));
            _documentId = null;
            // S2: nếu mở trong workspace (workspaceScopeId có giá trị), giữ nguyên _activeWorkspaceId
            // để scope workspace không bị xoá khi file tạm thời không đọc được.
            if (workspaceScopeId is null) _activeWorkspaceId = null;
            OnPropertyChanged(nameof(CurrentDocumentId));
            OnPropertyChanged(nameof(HasDocument));
            OnPropertyChanged(nameof(CanPromoteToWorkspace));
            Chat.LoadHistory();
            Notes.LoadFor(null);
            UpdateNotesDocumentContext();
            NotifyError($"Không thể mở file PDF: {ex.Message}");
            FilePath = null;
        }
#pragma warning restore CA1031
    }

    // S2: seam quyết định workspace scope khi mở tài liệu.
    // Nếu explicitWorkspaceId != null: dùng ngay (mở trong workspace). Ngược lại: lấy/tạo default.
    internal string ResolveWorkspaceScope(string? explicitWorkspaceId, string documentId, string title, long nowUnixMs)
        => explicitWorkspaceId ?? _workspaceStore.GetOrCreateDefaultForDocument(documentId, title, nowUnixMs).Id;

    private string? GetChatOwnerId() => IsWorkspaceSession ? _activeWorkspaceId : _documentId;

    private DocumentSnapshot GetDocumentSnapshot() => new(
        _documentId,
        _documentBlocks,
        CurrentPage,
        CurrentDocumentTitle());

    // Tên trong Library (người dùng có thể đã đổi tên); chưa có trong Library thì lấy tên file.
    private string CurrentDocumentTitle()
        => Library.FirstOrDefault(i => i.DocumentId == _documentId)?.Title
            ?? System.IO.Path.GetFileNameWithoutExtension(FilePath ?? string.Empty);

    [RelayCommand]
    private void OpenSettings()
    {
        AiModelSettings before = _settingsService.GetModelSettings();
        string? embeddingKeyBefore = _settingsService.GetApiKey(before.EmbeddingProvider); // chỉ giữ trong biến cục bộ, không log
        var window = new SettingsWindow(new SettingsViewModel(_settingsService))
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true) OnSettingsSaved(before, embeddingKeyBefore);
    }

    // Seam sau khi lưu Settings (test gọi trực tiếp, không cần cửa sổ): cập nhật nhãn provider ở khung chat,
    // và chỉ khi Embedding Provider hoặc Model đổi, hoặc key embedding được thêm hay thay (tài liệu mở khi key
    // thiếu hoặc sai đang ở TextOnly hay lỗi), mới lập chỉ mục tài liệu đang mở (model đã Complete thì
    // DocumentIndexingService bỏ qua). Không đổi thì không huỷ lượt lập chỉ mục đang chạy.
    internal void OnSettingsSaved(AiModelSettings before, string? embeddingKeyBefore)
    {
        Chat.RefreshActiveModel();
        AiModelSettings after = _settingsService.GetModelSettings();
        bool embeddingChanged = after.EmbeddingProvider != before.EmbeddingProvider || after.EmbeddingModel != before.EmbeddingModel;
        string? embeddingKeyAfter = _settingsService.GetApiKey(after.EmbeddingProvider);
        bool embeddingKeyChanged = !string.IsNullOrEmpty(embeddingKeyAfter)
            && !string.Equals(embeddingKeyBefore, embeddingKeyAfter, StringComparison.Ordinal);
        if (embeddingChanged || embeddingKeyChanged)
            Indexing.StartBackgroundIndexing();
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
        // Kiểm trước để AddNote lỗi store (đã tự báo) không bị thông báo "chưa mở tài liệu" ghi đè.
        if (!Notes.CanAddNote)
        {
            NotifyError("Hãy mở một tài liệu để lưu ghi chú");
            return;
        }
        if (Notes.AddNote(msg.Content, null, null)) NotifySuccess("Đã lưu vào ghi chú");
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
        Indexing.Dispose();
        _documentIndex.Dispose();
        _documentService.Dispose();
        Chat.Dispose();
        GC.SuppressFinalize(this);
    }
}
