using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Phiên đọc (xem GLOSSARY): module duy nhất thay Document đang đọc. Sở hữu tập Tab, Open Set persistence,
/// Workspace đang hoạt động và state của Document đang đọc; nhận intent và tự giữ thứ tự các bước,
/// còn Search, Chat, Notes, Indexing và surface chỉ thực thi (qua interface hẹp truyền ở constructor).
/// </summary>
public sealed partial class ReadingSessionViewModel : ObservableObject
{
    private readonly IDocumentLoader _loader;
    private readonly IWorkspaceStore _workspaceStore;
    private readonly IReadingSessionSearch _search;
    private readonly IReadingSessionChat _chat;
    private readonly IReadingSessionNotes _notes;
    private readonly IReadingSessionIndexing _indexing;
    private readonly IReadingSessionSurface _surface;
    private readonly Action<string> _notifyError;
    private readonly LibraryDocumentLookup _findDocument;
    private readonly CompositionLookup _findComposition;
    private readonly TimeProvider _clock;
    private readonly OpenSetPersistence _openSet;
    private readonly Action<string>? _markDocumentOpened;
    private JumpOrigin? _jumpOrigin;

    // Tab đang được theo dõi view-state để toolbar (trang, zoom, tổng trang) khớp với viewer của nó.
    private OpenTab? _subscribedTab;
    private int _standalonePage = 1;
    private int _standaloneTotalPages = 1;
    private double _standaloneZoom = 1.0;

    public ReadingSessionViewModel(ReadingSessionDependencies dependencies)
    {
        ArgumentNullException.ThrowIfNull(dependencies);
        _loader = dependencies.Loader;
        _workspaceStore = dependencies.WorkspaceStore;
        _search = dependencies.Search;
        _chat = dependencies.Chat;
        _notes = dependencies.Notes;
        _indexing = dependencies.Indexing;
        _surface = dependencies.Surface;
        _notifyError = dependencies.NotifyError;
        _findDocument = dependencies.FindDocument ?? (_ => null);
        _findComposition = dependencies.FindComposition ?? ((_, _) => null);
        _markDocumentOpened = dependencies.MarkDocumentOpened;
        _clock = dependencies.Clock ?? TimeProvider.System;

        // Phiên đọc là bên nghe duy nhất việc đổi Tab và gọi OpenSetPersistence: thứ tự "xử lý đổi Tab rồi mới lập lịch lưu
        // Open Set" nằm trong OnActiveTabChanged và handler TabsChanged, không phụ thuộc thứ tự đăng ký sự kiện.
        Tabs = new TabSetViewModel();
        _openSet = new OpenSetPersistence(
            _workspaceStore, Tabs, () => ActiveWorkspaceId, () => IsWorkspaceSession)
        {
            DispatcherProvider = dependencies.DispatcherProvider ?? (() => null),
        };
        Tabs.ActiveTabChanged += OnActiveTabChanged;
        Tabs.TabsChanged += OnTabsChanged;
    }

    /// <summary>Tập Tab (Open Set). Tab Strip chỉ đọc; mọi thao tác ghi đi qua Phiên đọc.</summary>
    public TabSetViewModel Tabs { get; }

    /// <summary>Text engine dùng chung để viewer tô từ khoá và Đoạn nguồn.</summary>
    public IPdfDocumentService MatchSource => _loader.MatchSource;

    /// <summary>Đang ở named Workspace (có Tab) thay vì đọc lẻ ở Default Workspace.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StandaloneDocumentSource))]
    [NotifyPropertyChangedFor(nameof(CanPromote))]
    public partial bool IsWorkspaceSession { get; private set; }

    /// <summary>Workspace đang hoạt động; null khi chưa có Document nào được nạp ở chế độ đọc lẻ.</summary>
    [ObservableProperty]
    public partial string? ActiveWorkspaceId { get; private set; }

    /// <summary>Đường dẫn file đang đọc; null khi nạp lỗi hoặc chưa có.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StandaloneDocumentSource))]
    public partial string? FilePath { get; private set; }

    // Nguồn cho viewer ĐỌC LẺ. Trong phiên Workspace: null để viewer đọc lẻ ngủ yên (không nạp lại, không ghi ngược
    // zoom/trang qua binding TwoWay vào Tab active, bug #44). FilePath vẫn giữ nguyên cho lập chỉ mục.
    public string? StandaloneDocumentSource => IsWorkspaceSession ? null : FilePath;

    /// <summary>Đoạn nguồn (trích dẫn AI) đang tô; null khi không có.</summary>
    [ObservableProperty]
    public partial SourcePassage? ActiveSourcePassage { get; set; }

    /// <summary>Id Document đang đọc; null khi chưa có hoặc nạp lỗi.</summary>
    public string? DocumentId { get; private set; }

    /// <summary>Có Document đang đọc hay không (bật nút quay lại trình đọc).</summary>
    public bool HasDocument => DocumentId is not null;

    public IReadOnlyList<TextBlock> DocumentBlocks { get; private set; } = [];

    internal IReadOnlyList<PageText> PageTexts { get; private set; } = [];

    // Origin của Jump đang chạy (null ngoài Jump): Tab đổi và nạp chạy lồng bên trong Jump nên đọc được nó ở đó,
    // không phụ thuộc cờ do bên ngoài đặt.
    private bool KeepsSearch => _jumpOrigin == JumpOrigin.Search;

    // Tab Document mà Phiên đọc đang bám: Tab active nếu là Tab Document, còn Tab Bản soạn active thì là Tab Document gần nhất
    // (Tab Bản soạn không đổi Phiên đọc, ADR 0009).
    private DocumentTab? _lastDocumentTab;
    private bool _documentKeptWhileComposing;

    private OpenTab? ActiveViewTab => IsWorkspaceSession ? (Tabs.ActiveTab as DocumentTab ?? _lastDocumentTab) : null;

    /// <summary>Tab Bản soạn đang active (Search của viewer tắt, viewer Document ẩn).</summary>
    public bool IsCompositionTabActive => Tabs.ActiveTab is CompositionTab;

    /// <summary>Intent: mở <paramref name="composition"/> thành Tab Bản soạn active (đã mở thì kích hoạt, không tạo tab trùng).
    /// Không đổi Phiên đọc.</summary>
    public CompositionTab OpenComposition(Composition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return Tabs.OpenOrActivateComposition(composition);
    }

    // Trong phiên Workspace có Tab active: trang, zoom, tổng trang là MỘT nguồn sự thật với OpenTab (viewer per-tab bind cùng
    // OpenTab nên toolbar và viewer luôn đồng bộ). Đọc lẻ: dùng field riêng.
    public int CurrentPage
    {
        get => ActiveViewTab is { } tab ? tab.Page : _standalonePage;
        set
        {
            if (ActiveViewTab is { } tab) { tab.Page = value; }   // OpenTab.PropertyChanged -> re-raise
            else if (_standalonePage != value) { _standalonePage = value; OnPropertyChanged(); }
        }
    }

    public int TotalPages
    {
        get => ActiveViewTab is { } tab ? tab.TotalPages : _standaloneTotalPages;
        set
        {
            if (ActiveViewTab is { } tab) { tab.TotalPages = value; }
            else if (_standaloneTotalPages != value) { _standaloneTotalPages = value; OnPropertyChanged(); }
        }
    }

    public double ZoomLevel
    {
        get => ActiveViewTab is { } tab ? tab.Zoom : _standaloneZoom;
        set
        {
            if (ActiveViewTab is { } tab) { tab.Zoom = value; }
            else if (_standaloneZoom != value) { _standaloneZoom = value; OnPropertyChanged(); }
        }
    }

    /// <summary>Intent: kích hoạt <paramref name="tab"/>. Huỷ Chat, lưu rồi khôi phục UI panel notes theo Tab,
    /// reset Search, bỏ Đoạn nguồn, nạp Document của Tab.</summary>
    public void Activate(OpenTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Tabs.ActivateTab(tab);
    }

    /// <summary>Intent: đóng <paramref name="tab"/> (chỉ gỡ khỏi Open Set, không gỡ membership). Đóng Tab active thì
    /// Tab dùng gần nhất được kích hoạt; đóng Tab cuối thì surface được làm mới và không Document nào được nạp.</summary>
    public void Close(OpenTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        Tabs.Close(tab);
    }

    /// <summary>Intent: đóng Tab của <paramref name="documentId"/> nếu Document đang mở thành Tab; không thì không làm gì.</summary>
    public void CloseDocument(string documentId)
    {
        var tab = Tabs.Tabs.FirstOrDefault(t => t is DocumentTab && t.ItemId == documentId);
        if (tab is not null) Close(tab);
    }

    /// <summary>
    /// Intent: Cross-doc jump tới trang của một Document trong Library. Đang ở Workspace thì kích hoạt hoặc mở Tab của
    /// Document; đọc lẻ thì Document thay Document đang đọc (không tạo Tab). Theo <see cref="JumpRequest.Origin"/>: Search
    /// giữ ô tìm, kết quả và Phạm vi, Note và Citation reset Search; Citation đặt Đoạn nguồn sau khi nạp thành công,
    /// và nhảy tới Document đã gỡ khỏi Library thì không nhảy và không có Đoạn nguồn nào được tô.
    /// </summary>
    public void Jump(JumpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        bool isCitation = request.Origin == JumpOrigin.Citation;
        var item = _findDocument(request.DocumentId);
        if (item is null)
        {
            if (isCitation) ActiveSourcePassage = null;   // đoạn của trích dẫn trước không được tô nhầm chỗ
            return;
        }

        int targetPage = request.PageIndex + 1;
        _jumpOrigin = request.Origin;
        try
        {
            if (IsWorkspaceSession)
            {
                // Activate-or-open + đặt trang đích TRƯỚC khi nạp (initialPage) -> viewer mở thẳng tại trang.
                Tabs.OpenOrActivate(request.DocumentId, item.Title, item.StoredPath, targetPage);
            }
            else
            {
                ReplaceCurrentDocument(item.StoredPath, ActiveWorkspaceId, targetPage);
            }
        }
        finally
        {
            _jumpOrigin = null;
        }

        // Nạp xoá Đoạn nguồn cũ nên đặt sau. Giữ hành vi hiện có: nạp lỗi vẫn đặt Đoạn nguồn (viewer không có gì để tô).
        if (isCitation) ActiveSourcePassage = request.SourcePassage;
    }

    /// <summary>Jump từ một Note; Note không mang trang thì mở trang đầu.</summary>
    public void JumpFromNote(string documentId, int? pageIndex)
        => Jump(new JumpRequest(documentId, pageIndex ?? 0, JumpOrigin.Note));

    /// <summary>Jump từ kết quả tìm (<paramref name="pageIndex"/> tính từ 0).</summary>
    public void JumpFromSearch(string documentId, int pageIndex)
        => Jump(new JumpRequest(documentId, pageIndex, JumpOrigin.Search));

    /// <summary>Jump từ trích dẫn AI: Đoạn nguồn lấy từ <paramref name="source"/> và <paramref name="quote"/>.</summary>
    public void JumpFromCitation(RagSource source, string? quote)
    {
        ArgumentNullException.ThrowIfNull(source);
        Jump(new JumpRequest(source.DocumentId, source.PageIndex, JumpOrigin.Citation, SourcePassage.From(source, quote)));
    }

    /// <summary>
    /// Intent: vào Workspace <paramref name="workspaceId"/>. Lưu Open Set của Workspace đang rời, rồi khôi phục lười Open Set
    /// đã lưu (chỉ Tab active nạp Document, Document đã gỡ khỏi Library bị bỏ qua) hoặc seed một Tab khi chưa có state
    /// (Document mặc định, rồi thành viên đầu). Workspace rỗng thì Open Set rỗng. Trong lúc dựng không ghi Open Set;
    /// chỉ ghi đúng một lần khi xong.
    /// </summary>
    public void RestoreOpenSet(string workspaceId)
    {
        ArgumentException.ThrowIfNullOrEmpty(workspaceId);
        if (IsWorkspaceSession && ActiveWorkspaceId is not null) _openSet.SaveNow();

        // Đọc state đã lưu TRƯỚC khi Reset để việc xoá Open Set cũ không ghi đè nó.
        var savedTabs = _workspaceStore.GetOpenTabs(workspaceId);

        _openSet.IsRestoring = true;
        try
        {
            Tabs.Reset();   // bắt đầu từ Open Set rỗng: không tích luỹ, không trùng Tab
            ActiveWorkspaceId = workspaceId;
            IsWorkspaceSession = true;

            var restored = RestoreSavedTabs(workspaceId, savedTabs);
            if (restored.Count > 0)
            {
                string? activeId = savedTabs.LastOrDefault(s => s.IsActive && restored.Any(t => t.ItemId == s.ItemId))?.ItemId;
                // Tab Bản soạn không đổi Phiên đọc: nạp Tab Document gần nhất trước để Phiên đọc bám nó, rồi mới kích hoạt Bản soạn.
                var keptDocument = restored.FirstOrDefault(t => t.ItemId == activeId) is CompositionTab active
                    ? NearestDocumentTab(restored, active)
                    : null;
                Tabs.RestoreTabs(restored, keptDocument?.ItemId ?? activeId ?? restored[0].ItemId);
                if (keptDocument is not null) Tabs.ActivateTab(restored.First(t => t.ItemId == activeId));
            }
            else
            {
                SeedFirstTab(workspaceId);
            }
        }
        finally
        {
            _openSet.IsRestoring = false;
            _openSet.SaveNow();   // đúng một lần ghi Open Set mới (đặc biệt khi seed tạo Tab)
        }
    }

    // Tab Document gần nhất trước tab Bản soạn, không có thì Tab Document đầu tiên.
    private static OpenTab? NearestDocumentTab(List<OpenTab> tabs, OpenTab composition)
    {
        int at = tabs.IndexOf(composition);
        return tabs.Take(at).LastOrDefault(t => t is DocumentTab) ?? tabs.FirstOrDefault(t => t is DocumentTab);
    }

    private List<OpenTab> RestoreSavedTabs(string workspaceId, IReadOnlyList<OpenTabState> savedTabs)
    {
        var restored = new List<OpenTab>(savedTabs.Count);
        foreach (var state in savedTabs)
        {
            if (state.Kind == TabKind.Composition)
            {
                // Chế độ Sửa không được nhớ: tab luôn mở lại ở chế độ đọc. Bản soạn đã xoá thì bỏ qua.
                if (_findComposition(workspaceId, state.ItemId) is { } composition)
                    restored.Add(new CompositionTab(composition) { ScrollNorm = state.ScrollNorm });
                continue;
            }

            if (_findDocument(state.ItemId) is not { } item) continue;   // Document đã gỡ khỏi Library
            restored.Add(new DocumentTab(state.ItemId, item.Title, item.StoredPath)
            {
                Page = state.Page,
                Zoom = state.Zoom,
                ScrollNorm = state.ScrollNorm,
            });
        }

        return restored;
    }

    // Chưa có Open Set đã lưu: Document mặc định, rồi thành viên đầu. Không có (hoặc đã gỡ khỏi Library) thì Open Set rỗng
    // và canvas hiện Workspace Documents surface inline.
    private void SeedFirstTab(string workspaceId)
    {
        var workspace = _workspaceStore.Get(workspaceId);
        var memberIds = _workspaceStore.GetDocumentIds(workspaceId);
        string? seedId = workspace?.DefaultDocumentId is { Length: > 0 } defaultId
            ? defaultId
            : memberIds.Count > 0 ? memberIds[0] : null;
        if (seedId is not null && _findDocument(seedId) is { } item)
            Tabs.OpenOrActivate(item.DocumentId, item.Title, item.StoredPath);
    }

    /// <summary>Intent: mở <paramref name="document"/> trong Workspace <paramref name="workspaceId"/> (Add hoặc click thành viên
    /// trên surface). Đã có Tab thì kích hoạt Tab đó, chưa có thì mở Tab mới và nạp. Thời điểm mở ghi vào Library (best-effort).</summary>
    public void OpenInWorkspace(string workspaceId, LibraryItem document)
    {
        ArgumentNullException.ThrowIfNull(workspaceId);
        ArgumentNullException.ThrowIfNull(document);
        // Phạm vi Workspace phải có trước khi Tab được kích hoạt và nạp.
        ActiveWorkspaceId = workspaceId;
        IsWorkspaceSession = true;
        Tabs.OpenOrActivate(document.DocumentId, document.Title, document.StoredPath);
        MarkOpenedBestEffort(document.DocumentId);
    }

    /// <summary>Intent: về trạng thái không có Workspace đang hoạt động (Workspace đang mở bị xoá). Open Set rỗng,
    /// Notes trống, surface modal đóng, không Document nào được nạp. Bên gọi bỏ chọn Workspace trước để lần làm mới
    /// surface do đóng Tab cuối không đọc dữ liệu của Workspace đã xoá.</summary>
    public void Reset()
    {
        ActiveWorkspaceId = null;
        IsWorkspaceSession = false;
        _surface.DismissModal();
        _notes.LoadFor(null);
        Tabs.Reset();
        SetDocumentState(null, []);
        FilePath = null;
        PageTexts = [];
    }

    private void MarkOpenedBestEffort(string documentId)
    {
#pragma warning disable CA1031 // best-effort: ghi thời điểm mở lỗi không chặn việc mở Document
        try { _markDocumentOpened?.Invoke(documentId); }
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"ReadingSessionViewModel: ghi thời điểm mở lỗi: {ex}"); }
#pragma warning restore CA1031
    }

    /// <summary>Có Document đang đọc lẻ để promote (không ở named Workspace).</summary>
    public bool CanPromote => !IsWorkspaceSession && DocumentId is not null;

    /// <summary>
    /// Intent: mở lẻ <paramref name="path"/> trong Default Workspace của Document. Thay Document đang đọc,
    /// không tạo Tab; Default Workspace được lấy lại hoặc tạo khi chưa có. Nạp lỗi thì báo lỗi và không còn
    /// Document hay Workspace đang hoạt động.
    /// </summary>
    public void Open(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        IsWorkspaceSession = false;
        ReplaceCurrentDocument(path);
    }

    /// <summary>
    /// Intent: biến Document đang đọc lẻ thành Tab đầu tiên và duy nhất của <paramref name="workspace"/> (named Workspace mới),
    /// mang theo trang và zoom. Trả về false khi không có Document đọc lẻ để promote.
    /// </summary>
    /// <param name="workspace">Named Workspace mới, chưa lưu.</param>
    /// <param name="documentTitle">Tiêu đề Tab của Document.</param>
    public bool Promote(Workspace workspace, string documentTitle)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        if (!CanPromote || DocumentId is not { } documentId || FilePath is not { } path) return false;

        // Đọc trang và zoom TRƯỚC khi chuyển sang phiên Workspace, vì sau đó chúng đọc từ Tab.
        int page = CurrentPage;
        double zoom = ZoomLevel;

        _workspaceStore.Upsert(workspace);
        _workspaceStore.AddDocument(workspace.Id, documentId);
        ActiveWorkspaceId = workspace.Id;
        IsWorkspaceSession = true;

        var tab = Tabs.OpenOrActivate(documentId, documentTitle, path, initialPage: page);
        tab.Zoom = zoom;   // sau khi kích hoạt: Tab mới có zoom mặc định
        return true;
    }

    /// <summary>Lưu Open Set ngay (đóng cửa sổ).</summary>
    public void SaveOpenSetNow() => _openSet.SaveNow();

    /// <summary>Document vừa nạp cần Workspace phạm vi: <paramref name="ExplicitWorkspaceId"/> nếu có, không thì Default Workspace của nó.</summary>
    private sealed record WorkspaceScopeRequest(string? ExplicitWorkspaceId, string DocumentId, string Title);

    /// <summary>Mục tiêu lập chỉ mục của Document đang đọc; null khi chưa có Document.</summary>
    public IndexingTarget? CurrentIndexingTarget
        => DocumentId is null ? null : new IndexingTarget(DocumentId, FilePath, PageTexts);

    /// <summary>Lấy Workspace phạm vi khi nạp: Workspace chỉ định (mở trong Workspace) hoặc Default Workspace của Document.</summary>
    private string ResolveWorkspaceScope(WorkspaceScopeRequest request)
        => request.ExplicitWorkspaceId
            ?? _workspaceStore.GetOrCreateDefaultForDocument(
                request.DocumentId, request.Title, _clock.GetUtcNow().ToUnixTimeMilliseconds()).Id;

    /// <summary>
    /// Bước dùng chung "thay Document đang đọc": mọi intent đổi Document đều đi qua đây.
    /// </summary>
    /// <param name="path">File cần nạp.</param>
    /// <param name="workspaceScopeId">Workspace chỉ định; null thì dùng Default Workspace của Document.</param>
    /// <param name="initialPage">Trang mở đầu (từ 1).</param>
    private void ReplaceCurrentDocument(string path, string? workspaceScopeId = null, int initialPage = 1)
    {
        ActiveSourcePassage = null;   // đoạn nguồn thuộc tài liệu cũ; Jump(Citation) đặt lại sau khi nạp
        // Đặt trang đích TRƯỚC khi gán FilePath: gán FilePath làm viewer nạp và bố cục ngay theo CurrentPage,
        // nên mở thẳng tại trang neo (tránh nạp-rồi-nhảy bị reset ghi đè).
        CurrentPage = initialPage;
        FilePath = path;
        try
        {
            ApplyLoaded(_loader.Load(path), path, workspaceScopeId);
        }
#pragma warning disable CA1031 // ranh giới intent: nạp tài liệu lỗi thì báo cho người dùng
        catch (Exception ex)
        {
            ApplyLoadFailure(ex, workspaceScopeId);
        }
#pragma warning restore CA1031
    }

    private void ApplyLoaded(LoadedDocument loaded, string path, string? workspaceScopeId)
    {
        PageTexts = loaded.PageTexts;
        SetDocumentState(loaded.DocumentId, loaded.Blocks);

        string title = System.IO.Path.GetFileNameWithoutExtension(path);
        ActiveWorkspaceId = ResolveWorkspaceScope(new WorkspaceScopeRequest(ExplicitWorkspaceId: workspaceScopeId, DocumentId: loaded.DocumentId, Title: title));
        _chat.LoadHistory();
        _notes.LoadFor(ActiveWorkspaceId);
        _notes.RefreshDocumentContext();
        if (!KeepsSearch) _search.ClearResults();
        _indexing.StartBackgroundIndexing();
    }

    private void ApplyLoadFailure(Exception ex, string? workspaceScopeId)
    {
        SetDocumentState(null, []);
        // Mở trong Workspace (workspaceScopeId có giá trị) thì giữ Workspace để scope không mất khi file tạm thời không đọc được.
        if (workspaceScopeId is null) ActiveWorkspaceId = null;
        _chat.LoadHistory();
        _notes.LoadFor(null);
        _notes.RefreshDocumentContext();
        _notifyError($"Không thể mở file PDF: {ex.Message}");
        FilePath = null;
    }

    // Trạng thái Document đang đọc (id và khối text) đổi cùng lúc ở nạp thành công lẫn nạp lỗi.
    private void SetDocumentState(string? documentId, IReadOnlyList<TextBlock> blocks)
    {
        DocumentBlocks = blocks;
        OnPropertyChanged(nameof(DocumentBlocks));
        DocumentId = documentId;
        OnPropertyChanged(nameof(DocumentId));
        OnPropertyChanged(nameof(HasDocument));
        OnPropertyChanged(nameof(CanPromote));
    }

    private void OnTabsChanged()
    {
        // Đóng Tab Document mà Phiên đọc đang bám trong lúc Tab Bản soạn active không đổi Tab active, nên không có
        // ActiveTabChanged: xử lý như Tab Bản soạn vào lại để Phiên đọc rỗng hoặc bám tab khác.
        if (_documentKeptWhileComposing && _lastDocumentTab is not null && !Tabs.Tabs.Contains(_lastDocumentTab))
        {
            OnActiveTabChanged(Tabs.ActiveTab);
            return;
        }

        _openSet.Schedule();
    }

    private void OnActiveTabChanged(OpenTab? incoming)
    {
        bool resumingKeptDocument = _documentKeptWhileComposing && incoming is DocumentTab resumed && ReferenceEquals(resumed, _lastDocumentTab);
        _documentKeptWhileComposing = false;
        OnPropertyChanged(nameof(IsCompositionTabActive));

        if (incoming is CompositionTab)
        {
            // Tab Bản soạn không đổi Phiên đọc: giữ Document, Chat, panel Notes và Đoạn nguồn của Tab Document gần nhất.
            if (_lastDocumentTab is not null && Tabs.Tabs.Contains(_lastDocumentTab))
            {
                _documentKeptWhileComposing = true;
                _openSet.Schedule();
                return;
            }

            incoming = null;   // không còn Tab Document nào: Phiên đọc rỗng như khi Open Set rỗng
        }
        else if (resumingKeptDocument)
        {
            RaiseViewStateChanged();
            _openSet.Schedule();
            return;
        }

        _lastDocumentTab = incoming as DocumentTab;
        // Đổi hoặc đóng Tab: câu trả lời đang chạy thuộc tài liệu của Tab cũ (kể cả khi đóng Tab cuối, không nạp lại chat).
        _chat.CancelPendingRequest();
        SwapNotesPanelState(incoming);

        // Nhảy từ kết quả tìm Toàn Workspace: giữ ô tìm, kết quả và Phạm vi để bấm tiếp kết quả khác.
        if (!KeepsSearch) _search.Reset();

        // Tab mới (hoặc không còn Tab) thì Đoạn nguồn của tài liệu trước không còn nghĩa.
        ActiveSourcePassage = null;

        if (incoming is DocumentTab incomingDocument)
        {
            ReplaceCurrentDocument(incomingDocument.Path, ActiveWorkspaceId, incoming.Page);
        }
        else
        {
            SetDocumentState(null, []);
            FilePath = null;
            PageTexts = [];
            if (!_openSet.IsRestoring)
                RefreshSurface();   // đóng Tab cuối -> surface inline cần dữ liệu mới

        }

        RaiseViewStateChanged();

        // Lưu Open Set sau khi mọi bước xử lý đổi Tab ở trên xong (thứ tự do Phiên đọc quyết định).
        _openSet.Schedule();
    }

    // View-state của UI panel notes sống trên OpenTab: lưu vào Tab cũ, khôi phục từ Tab mới; đồng thời theo dõi view-state
    // của Tab mới để toolbar cập nhật khi viewer ghi Page, Zoom, TotalPages.
    private void SwapNotesPanelState(OpenTab? incoming)
    {
        if (_subscribedTab is not null)
        {
            _subscribedTab.PropertyChanged -= OnActiveTabViewStateChanged;
            var state = _notes.PanelState;
            _subscribedTab.NotesScroll = state.NotesScroll;
            _subscribedTab.SelectedNoteId = state.SelectedNoteId;
            _subscribedTab.FilterCurrentDocumentOnly = state.FilterCurrentDocumentOnly;
        }

        _subscribedTab = incoming;
        if (incoming is not null)
        {
            incoming.PropertyChanged += OnActiveTabViewStateChanged;
            _notes.PanelState = new NotesPanelState(incoming.NotesScroll, incoming.SelectedNoteId, incoming.FilterCurrentDocumentOnly);
        }
    }

    private void RefreshSurface()
    {
        _surface.Refresh();
        _notes.RefreshDocumentContext();
    }

    private void OnActiveTabViewStateChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(OpenTab.Page): OnPropertyChanged(nameof(CurrentPage)); break;
            case nameof(OpenTab.Zoom): OnPropertyChanged(nameof(ZoomLevel)); break;
            case nameof(OpenTab.TotalPages): OnPropertyChanged(nameof(TotalPages)); break;
        }

        _openSet.Schedule();
    }

    // Tab active hoặc chế độ đổi -> toolbar phải đọc lại view-state của nguồn mới.
    private void RaiseViewStateChanged()
    {
        OnPropertyChanged(nameof(CurrentPage));
        OnPropertyChanged(nameof(ZoomLevel));
        OnPropertyChanged(nameof(TotalPages));
    }

    partial void OnIsWorkspaceSessionChanged(bool value) => RaiseViewStateChanged();
}
