using System.Windows.Threading;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Lưu Open Set (danh sách tab, tab active, view-state) của Workspace đang hoạt động, có gộp ghi (debounce).</summary>
public sealed class OpenSetPersistence
{
    private static readonly TimeSpan DebounceInterval = TimeSpan.FromMilliseconds(400);

    private readonly IWorkspaceStore _workspaceStore;
    private readonly TabSetViewModel _tabs;
    private readonly Func<string?> _activeWorkspaceId;
    private readonly Func<bool> _isWorkspaceSession;
    private DispatcherTimer? _debounce;

    /// <param name="workspaceStore">Nơi lưu Open Set.</param>
    /// <param name="tabs">Tập tab cần lưu. Lớp này không tự nghe sự kiện: bên sở hữu gọi <see cref="Schedule"/> đúng thứ tự.</param>
    /// <param name="activeWorkspaceId">Id workspace đang hoạt động, null khi chưa có.</param>
    /// <param name="isWorkspaceSession">Đang ở phiên workspace (không phải đọc lẻ).</param>
    public OpenSetPersistence(
        IWorkspaceStore workspaceStore,
        TabSetViewModel tabs,
        Func<string?> activeWorkspaceId,
        Func<bool> isWorkspaceSession)
    {
        _workspaceStore = workspaceStore;
        _tabs = tabs;
        _activeWorkspaceId = activeWorkspaceId;
        _isWorkspaceSession = isWorkspaceSession;
    }

    /// <summary>
    /// Dispatcher để gộp ghi; trả về null (test/headless) thì lưu ngay để hành vi tất định.
    /// Tách khỏi constructor để nơi dựng không phải biết kiểu <see cref="Dispatcher"/>.
    /// </summary>
    internal Func<Dispatcher?> DispatcherProvider { get; init; } = () => null;

    /// <summary>Khoá ghi trong lúc reset/khôi phục để wiring auto-save không ghi đè state đang dựng.</summary>
    internal bool IsRestoring { get; set; }

    /// <summary>Thu thập Open Set hiện tại và lưu (thay thế) cho workspace đang hoạt động.</summary>
    public void SaveNow()
    {
        var workspaceId = _activeWorkspaceId();
        if (workspaceId is null || !_isWorkspaceSession()) return;
        long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var activeTab = _tabs.ActiveTab;
        var states = new List<OpenTabState>(_tabs.Tabs.Count);
        for (int order = 0; order < _tabs.Tabs.Count; order++)
        {
            var tab = _tabs.Tabs[order];
            states.Add(new OpenTabState(
                tab.DocumentId, order, ReferenceEquals(tab, activeTab),
                tab.Page, tab.Zoom, tab.ScrollNorm, nowUnixMs));
        }
        try { _workspaceStore.SaveOpenTabs(workspaceId, states); }
#pragma warning disable CA1031 // best-effort: lưu tab lỗi không được làm gãy phiên đọc
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"OpenSetPersistence.SaveNow: lưu tab lỗi: {ex}"); }
#pragma warning restore CA1031
    }

    /// <summary>Gộp nhiều thay đổi liên tiếp (cuộn/zoom) thành một lần ghi.</summary>
    public void Schedule()
    {
        if (IsRestoring) return;
        if (_activeWorkspaceId() is null || !_isWorkspaceSession()) return;
        var dispatcher = DispatcherProvider();
        if (dispatcher is null) { SaveNow(); return; }
        _debounce ??= new DispatcherTimer(
            DebounceInterval, DispatcherPriority.Background,
            (_, _) => { _debounce!.Stop(); SaveNow(); }, dispatcher);
        _debounce.Stop();
        _debounce.Start();
    }
}
