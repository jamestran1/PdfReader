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
    private readonly OpenSetPersistence _openSet;

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

        // Phiên đọc là bên nghe duy nhất việc đổi Tab active, và đăng ký TRƯỚC OpenSetPersistence trong cùng constructor,
        // nên thứ tự "xử lý đổi Tab rồi mới lưu Open Set" nằm ở một chỗ. Sự kiện còn cần vì OpenOrActivate, RestoreTabs và
        // Reset của TabSetViewModel vẫn đổi Tab active từ các intent chưa chuyển vào Phiên đọc.
        Tabs = new TabSetViewModel();
        Tabs.ActiveTabChanged += OnActiveTabChanged;
        _openSet = new OpenSetPersistence(
            _workspaceStore, Tabs, () => ActiveWorkspaceId, () => IsWorkspaceSession)
        {
            DispatcherProvider = dependencies.DispatcherProvider ?? (() => System.Windows.Application.Current?.Dispatcher),
        };
    }

    /// <summary>Tập Tab (Open Set). Tab Strip chỉ đọc; mọi thao tác ghi đi qua Phiên đọc.</summary>
    public TabSetViewModel Tabs { get; }

    /// <summary>Text engine dùng chung để viewer tô từ khoá và Đoạn nguồn.</summary>
    public IPdfDocumentService MatchSource => _loader.MatchSource;

    /// <summary>Đang ở named Workspace (có Tab) thay vì đọc lẻ ở Default Workspace.</summary>
    [ObservableProperty]
    public partial bool IsWorkspaceSession { get; internal set; }

    /// <summary>Workspace đang hoạt động; null khi chưa có Document nào được nạp ở chế độ đọc lẻ.</summary>
    [ObservableProperty]
    public partial string? ActiveWorkspaceId { get; internal set; }

    /// <summary>Đường dẫn file đang đọc; null khi nạp lỗi hoặc chưa có.</summary>
    [ObservableProperty]
    public partial string? FilePath { get; internal set; }

    /// <summary>Đoạn nguồn (trích dẫn AI) đang tô; null khi không có.</summary>
    [ObservableProperty]
    public partial SourcePassage? ActiveSourcePassage { get; set; }

    /// <summary>Id Document đang đọc; null khi chưa có hoặc nạp lỗi.</summary>
    public string? DocumentId { get; private set; }

    public IReadOnlyList<TextBlock> DocumentBlocks { get; private set; } = [];

    internal IReadOnlyList<PageText> PageTexts { get; private set; } = [];

    /// <summary>Tạm thời: cờ giữ Search khi Tab đổi do kết quả tìm Toàn Workspace. Bị thay bằng origin của intent Jump.</summary>
    internal bool KeepSearchOnTabChange { get; set; }

    private OpenTab? ActiveViewTab => IsWorkspaceSession ? Tabs.ActiveTab : null;

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

    /// <summary>Tạm thời: khoá khôi phục của Open Set cho <c>RestoreOrSeedOpenSet</c> của MainViewModel; biến mất khi
    /// intent RestoreOpenSet chuyển vào Phiên đọc.</summary>
    internal OpenSetPersistence OpenSet => _openSet;

    /// <summary>Lưu Open Set ngay (đóng cửa sổ).</summary>
    internal void SaveOpenSetNow() => _openSet.SaveNow();

    /// <summary>Lấy Workspace phạm vi khi nạp: Workspace chỉ định (mở trong Workspace) hoặc Default Workspace của Document.</summary>
    internal string ResolveWorkspaceScope(string? explicitWorkspaceId, string documentId, string title, long nowUnixMs)
        => explicitWorkspaceId ?? _workspaceStore.GetOrCreateDefaultForDocument(documentId, title, nowUnixMs).Id;

    /// <summary>
    /// Bước dùng chung "thay Document đang đọc": mọi intent đổi Document đều đi qua đây. Tạm thời internal cho đến khi
    /// các intent đọc lẻ và Jump chuyển vào Phiên đọc.
    /// </summary>
    /// <param name="path">File cần nạp.</param>
    /// <param name="workspaceScopeId">Workspace chỉ định; null thì dùng Default Workspace của Document.</param>
    /// <param name="initialPage">Trang mở đầu (từ 1).</param>
    internal void ReplaceCurrentDocument(string path, string? workspaceScopeId = null, int initialPage = 1)
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
        DocumentBlocks = loaded.Blocks;
        OnPropertyChanged(nameof(DocumentBlocks));
        PageTexts = loaded.PageTexts;
        SetDocumentId(loaded.DocumentId);

        long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string title = System.IO.Path.GetFileNameWithoutExtension(path);
        ActiveWorkspaceId = ResolveWorkspaceScope(workspaceScopeId, loaded.DocumentId, title, nowMs);
        _chat.LoadHistory();
        _notes.LoadFor(ActiveWorkspaceId);
        _notes.RefreshDocumentContext();
        // Nạp chạy đồng bộ trong lúc đổi Tab nên cờ nhảy-từ-kết-quả-tìm vẫn còn hiệu lực ở đây.
        if (!KeepSearchOnTabChange) _search.ClearResults();
        _indexing.StartBackgroundIndexing();
    }

    private void ApplyLoadFailure(Exception ex, string? workspaceScopeId)
    {
        DocumentBlocks = [];
        OnPropertyChanged(nameof(DocumentBlocks));
        SetDocumentId(null);
        // Mở trong Workspace (workspaceScopeId có giá trị) thì giữ Workspace để scope không mất khi file tạm thời không đọc được.
        if (workspaceScopeId is null) ActiveWorkspaceId = null;
        _chat.LoadHistory();
        _notes.LoadFor(null);
        _notes.RefreshDocumentContext();
        _notifyError($"Không thể mở file PDF: {ex.Message}");
        FilePath = null;
    }

    private void SetDocumentId(string? documentId)
    {
        DocumentId = documentId;
        OnPropertyChanged(nameof(DocumentId));
    }

    private void OnActiveTabChanged(OpenTab? incoming)
    {
        // Đổi hoặc đóng Tab: câu trả lời đang chạy thuộc tài liệu của Tab cũ (kể cả khi đóng Tab cuối, không nạp lại chat).
        _chat.CancelPendingRequest();
        SwapNotesPanelState(incoming);

        // Nhảy từ kết quả tìm Toàn Workspace: giữ ô tìm, kết quả và Phạm vi để bấm tiếp kết quả khác.
        if (!KeepSearchOnTabChange) _search.Reset();

        // Tab mới (hoặc không còn Tab) thì Đoạn nguồn của tài liệu trước không còn nghĩa.
        ActiveSourcePassage = null;

        if (incoming is not null)
            ReplaceCurrentDocument(incoming.Path, ActiveWorkspaceId, incoming.Page);
        else if (!_openSet.IsRestoring)
            RefreshSurface();   // đóng Tab cuối -> surface inline cần dữ liệu mới

        RaiseViewStateChanged();
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
