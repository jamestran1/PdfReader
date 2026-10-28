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

    /// <summary>Phiên đọc: nơi duy nhất thay Document đang đọc, sở hữu tập Tab và Open Set.</summary>
    public ReadingSessionViewModel Session { get; private set; } = null!;   // dựng cuối constructor, sau mọi bên cộng tác

    public TabSetViewModel Tabs => Session.Tabs;

    public bool IsWorkspaceSession
    {
        get => Session.IsWorkspaceSession;
        private set => Session.IsWorkspaceSession = value;
    }

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
    public IReadOnlyList<TextBlock> DocumentBlocks => Session.DocumentBlocks;

    // Exposed so PdfViewerControl can ask iText for exact keyword match rectangles to highlight.
    public IPdfDocumentService PdfService => Session.MatchSource;

    public SearchViewModel Search { get; }

    [ObservableProperty]
    public partial string WindowTitle { get; set; } = "Trí Thư";

    public string? FilePath => Session.FilePath;

    // Nguồn tài liệu cho viewer ĐỌC LẺ. Trong phiên Workspace: null để viewer đọc-lẻ ngủ yên
    // (không nạp lại, không ghi-ngược zoom/trang qua binding TwoWay vào tab active — bug #44).
    // FilePath vẫn giữ nguyên cho lập chỉ mục/nội bộ.
    public string? StandaloneDocumentSource => IsWorkspaceSession ? null : FilePath;

    // Toolbar: trang, zoom, tổng trang là state của Phiên đọc (một nguồn sự thật với Tab active, fix #44).
    public int CurrentPage
    {
        get => Session.CurrentPage;
        set => Session.CurrentPage = value;
    }

    public int TotalPages
    {
        get => Session.TotalPages;
        set => Session.TotalPages = value;
    }

    public double ZoomLevel
    {
        get => Session.ZoomLevel;
        set => Session.ZoomLevel = value;
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
    public string? ActiveWorkspaceId => Session.ActiveWorkspaceId;

    // S3: expose documentId hiện tại để bind vào PdfViewerControl.CurrentDocumentId (lọc highlight)
    public string? CurrentDocumentId => Session.DocumentId;

    // #38: có tài liệu đang mở hay không -> bật nút quay lại trình đọc trên nav rail.
    public bool HasDocument => Session.DocumentId != null;

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
        IThemeService? themeService = null,
        IDocumentLoader? documentLoader = null)
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
            DefaultServices.CreateLibrary(AppDataDirectory.Current()),
            documentLoader))
    { }

    public MainViewModel(MainViewModelServices services)
    {
        _documentService = services.DocumentService;
        _settingsService = services.Settings;
        _themeService = services.ThemeService;
        IsDarkMode = _settingsService.GetThemePreference() == AppTheme.Dark;
        var documentLoader = services.DocumentLoader ?? new PdfDocumentLoader(_documentService);

        _documentIndex = services.DocumentIndex;
        _documentIndex.EnsureSchema();
        Indexing = new IndexingViewModel(
            _documentIndex,
            new DocumentIndexingService(_documentIndex, services.EmbeddingFactory, _settingsService),
            () => Session.DocumentId is null ? null : new IndexingTarget(Session.DocumentId, FilePath, Session.PageTexts));

        _library = services.Library;
        ReloadLibrary();

        Chat = ChatViewModel.Create(services, GetChatContext, GetWorkspaceDocuments, OpenCitationSource);

        _noteStore = services.NoteStore;
        _noteStore.EnsureSchema();
        Notes = new NotesViewModel(_noteStore,
            () => Session.DocumentId is null ? (int?)null : CurrentPage - 1,
            idx => CurrentPage = idx + 1,
            () => Session.DocumentId,
            OpenDocumentForNote,
            NotifyError);
        Search = new SearchViewModel(
            _documentIndex, () => Session.DocumentId, page => CurrentPage = page, NotifyError,
            new SearchWorkspaceContext(GetSearchDocuments, JumpToDocumentFromSearch));

        _workspaceStore = services.WorkspaceStore;
        _workspaceStore.EnsureSchema();

        MigrateLegacyNotesToDefaultWorkspace();

        DocumentsSurface = CreateDocumentsSurface();

        // Dựng Phiên đọc sau mọi bên cộng tác; các lambda ở trên chỉ chạy sau khi Session đã có.
        Session = CreateSession(documentLoader);

        ReloadWorkspaces();
        Chat.LoadHistory();
    }

    private ReadingSessionViewModel CreateSession(IDocumentLoader documentLoader)
    {
        var session = new MainViewModelSessionFactory(
            documentLoader, _workspaceStore, Search, Chat, Notes, Indexing, DocumentsSurface)
            .Create(UpdateNotesDocumentContext, NotifyError,
                documentId => _library.MarkOpened(documentId, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                id => Library.FirstOrDefault(i => i.DocumentId == id));
        session.PropertyChanged += OnSessionPropertyChanged;
        return session;
    }

    // Báo lại cho XAML các property của MainViewModel phụ thuộc state của Phiên đọc.
    private void OnSessionPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(ReadingSessionViewModel.DocumentId):
                OnPropertyChanged(nameof(CurrentDocumentId));
                OnPropertyChanged(nameof(HasDocument));
                OnPropertyChanged(nameof(CanPromoteToWorkspace));
                break;
            case nameof(ReadingSessionViewModel.DocumentBlocks): OnPropertyChanged(nameof(DocumentBlocks)); break;
            case nameof(ReadingSessionViewModel.ActiveWorkspaceId): OnPropertyChanged(nameof(ActiveWorkspaceId)); break;
            case nameof(ReadingSessionViewModel.FilePath):
                OnPropertyChanged(nameof(FilePath));
                OnPropertyChanged(nameof(StandaloneDocumentSource));
                break;
            case nameof(ReadingSessionViewModel.ActiveSourcePassage): OnPropertyChanged(nameof(ActiveSourcePassage)); break;
            case nameof(ReadingSessionViewModel.CurrentPage): OnPropertyChanged(nameof(CurrentPage)); break;
            case nameof(ReadingSessionViewModel.TotalPages): OnPropertyChanged(nameof(TotalPages)); break;
            case nameof(ReadingSessionViewModel.ZoomLevel): OnPropertyChanged(nameof(ZoomLevel)); break;
            case nameof(ReadingSessionViewModel.IsWorkspaceSession):
                OnPropertyChanged(nameof(IsWorkspaceSession));
                OnPropertyChanged(nameof(StandaloneDocumentSource));
                OnPropertyChanged(nameof(IsDocumentTabStripVisible));
                OnPropertyChanged(nameof(CanPromoteToWorkspace));
                break;
        }
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
                if (openTab is not null) Session.Close(openTab);
            },
            notify: NotifySuccess,
            stateChanged: OnDocumentsSurfaceStateChanged);

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

    internal void SaveOpenSetNow() => Session.SaveOpenSetNow();

    // #68: chỉ hiện affordance promote khi đang đọc lẻ một tài liệu (Default Workspace).
    public bool CanPromoteToWorkspace => IsReadingDocument && !IsWorkspaceSession && HasDocument;

    // #68: promote Default Workspace -> named Workspace từ tài liệu đang đọc lẻ.
    // Tái dùng tạo workspace + AddDocument + OpenOrActivate; mang theo trang/zoom.
    [RelayCommand]
    private void PromoteToWorkspace(string? name)
    {
        if (!Session.CanPromote) return;
        if (string.IsNullOrWhiteSpace(name))
        {
            WorkspaceNameError = "Tên workspace không được để trống.";
            return;
        }
        WorkspaceNameError = string.Empty;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var workspace = new Workspace(Guid.NewGuid().ToString("N"), name.Trim(), false, null, now, now);
        SelectedWorkspace = workspace;
        var item = Library.FirstOrDefault(libraryItem => libraryItem.DocumentId == Session.DocumentId);
        string title = item?.Title ?? System.IO.Path.GetFileNameWithoutExtension(FilePath ?? string.Empty);
        Session.Promote(workspace, title);

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
        if (IsWorkspaceSession && Session.ActiveWorkspaceId is not null) SaveOpenSetNow();

        // Đọc state đã lưu TRƯỚC khi reset (tránh wiring auto-save ghi đè khi xoá Open Set cũ).
        var savedTabs = _workspaceStore.GetOpenTabs(workspaceId);

        Session.OpenSet.IsRestoring = true;
        try
        {
            Tabs.Reset();   // bắt đầu từ Open Set rỗng -> không tích lũy / không trùng tab (#bug 2,3)
            Session.ActiveWorkspaceId = workspaceId;
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
            Session.OpenSet.IsRestoring = false;
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
        if (Session.ActiveWorkspaceId is null)
        {
            return Session.DocumentId is null
                ? []
                : [new WorkspaceDocument(Session.DocumentId, CurrentDocumentTitle(), FilePath ?? string.Empty)];
        }

        var documents = new List<WorkspaceDocument>();
        foreach (string id in _workspaceStore.GetDocumentIds(Session.ActiveWorkspaceId))
        {
            var item = Library.FirstOrDefault(i => i.DocumentId == id);
            if (item is not null) documents.Add(new WorkspaceDocument(id, item.Title, item.StoredPath));
        }

        return documents;
    }

    private void JumpToDocumentFromSearch(string documentId, int pageIndex)
        => Session.JumpFromSearch(documentId, pageIndex);

    /// <summary>Đoạn nguồn của trích dẫn AI vừa mở, viewer của Document đó tô trên trang; null khi chưa mở trích dẫn
    /// nào hoặc nguồn không mang chữ của đoạn (lịch sử chat bản cũ).</summary>
    public SourcePassage? ActiveSourcePassage
    {
        get => Session.ActiveSourcePassage;
        set => Session.ActiveSourcePassage = value;
    }

    /// <summary>Mở nguồn của trích dẫn <c>[n]</c>: Cross-doc jump tới Document và trang được trích rồi tô đoạn nguồn.</summary>
    public void OpenCitationSource(RagSource source, string? quote = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        Session.JumpFromCitation(source, quote);
    }

    /// <summary>Tắt tô Đoạn nguồn (phím Esc).</summary>
    [RelayCommand]
    private void ClearSourcePassage() => ActiveSourcePassage = null;

    // Bấm note ở tài liệu khác: Phiên đọc lo Cross-doc jump; note không mang trang thì mở trang đầu.
    private void OpenDocumentForNote(string documentId, int? pageIndex) => Session.JumpFromNote(documentId, pageIndex);

    // S4: xóa một workspace (không được xóa default workspace)
    [RelayCommand]
    private void DeleteWorkspace(Workspace? ws)
    {
        if (ws is null) return;
        // Chặn xóa default workspace (default được ẩn khỏi danh sách; đây là phòng vệ)
        if (ws.IsDefault) return;
        _noteStore.DeleteForOwner(ws.Id);
        _workspaceStore.Delete(ws.Id);
        if (Session.ActiveWorkspaceId == ws.Id)
        {
            SelectedWorkspace = null;   // null trước Reset() — tránh Refresh stale qua đóng Tab cuối
            Session.Reset();
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
        Session.Activate(tab);
    }

    // Tab S1: đóng tab (chỉ gỡ khỏi Open Set, KHÔNG gỡ khỏi workspace membership).
    [RelayCommand]
    private void CloseTab(Models.OpenTab? tab)
    {
        if (tab is null) return;
        Session.Close(tab);
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
        // Tab S1: đảm bảo workspace scope được thiết lập trước khi tab được kích hoạt và nạp.
        Session.OpenInWorkspace(SelectedWorkspace.Id, item);
        ShowWorkspaces = false;
    }

    [RelayCommand]
    private void OpenLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        // Tab S1: standalone open -> không tích lũy tab; giữ hành vi thay thế cũ.
        Session.Open(item.StoredPath);
        if (Session.DocumentId != null)
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
                if (wsId == Session.ActiveWorkspaceId)
                {
                    var openTab = Tabs.Tabs.FirstOrDefault(t => t.DocumentId == docId);
                    if (openTab is not null) Session.Close(openTab);
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

    private ChatContext GetChatContext() => new(
        Session.DocumentId,
        IsWorkspaceSession ? Session.ActiveWorkspaceId : Session.DocumentId,
        Session.DocumentBlocks,
        CurrentPage,
        CurrentDocumentTitle());

    // Tên trong Library (người dùng có thể đã đổi tên); chưa có trong Library thì lấy tên file.
    private string CurrentDocumentTitle()
        => Library.FirstOrDefault(i => i.DocumentId == Session.DocumentId)?.Title
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
