using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Quản lý Open Set: tập các Tab đang mở trong phiên Workspace hiện tại.
/// Không phụ thuộc PDFium hay bất kỳ dịch vụ document nào -- thuần logic tab.
/// </summary>
public sealed class TabSetViewModel : ObservableObject
{
    /// <summary>Số tab inactive gần nhất (theo MRU) vẫn giữ handle PDFium, ngoài tab active (ADR 0003).</summary>
    public const int DefaultMaxWarmInactiveTabs = 5;

    private readonly int _maxWarmInactiveTabs;

    public TabSetViewModel(int maxWarmInactiveTabs = DefaultMaxWarmInactiveTabs)
    {
        _maxWarmInactiveTabs = maxWarmInactiveTabs;
        Tabs = new ReadOnlyObservableCollection<OpenTab>(_tabItems);
        _tabItems.CollectionChanged += (_, _) => TabsChanged?.Invoke();
    }

    private readonly ObservableCollection<OpenTab> _tabItems = [];

    /// <summary>Danh sách tab theo thứ tự hiển thị (Tab Strip order). Chỉ đọc: mọi thay đổi đi qua Phiên đọc.</summary>
    public ReadOnlyObservableCollection<OpenTab> Tabs { get; }

    private OpenTab? _activeTab;

    /// <summary>Tab đang kích hoạt. null nếu Open Set rỗng.</summary>
    public OpenTab? ActiveTab
    {
        get => _activeTab;
        private set
        {
            if (_activeTab == value) return;
            _activeTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasTabs));
            ActiveTabChanged?.Invoke(value);
        }
    }

    /// <summary>True khi có ít nhất một tab.</summary>
    public bool HasTabs => _tabItems.Count > 0;

    /// <summary>Phát khi ActiveTab thay đổi (kể cả khi thành null).</summary>
#pragma warning disable MA0046 // Sự kiện nội bộ dùng với lambda ở ViewModel và test; giữ chữ ký Action để không đổi hành vi
    public event Action<OpenTab?>? ActiveTabChanged;
#pragma warning restore MA0046

    /// <summary>Phát khi tập Tab đổi (thêm, bỏ, xoá hết); không phát khi chỉ đổi Tab active. Che kiểu collection bên trong khỏi người nghe.</summary>
#pragma warning disable MA0046 // cùng lý do với ActiveTabChanged
    public event Action? TabsChanged;
#pragma warning restore MA0046

    // Danh sách MRU (most-recently-activated): phần tử đầu = được kích hoạt gần nhất.
    private readonly List<OpenTab> _mru = [];

    /// <summary>
    /// Mở tab cho <paramref name="documentId"/>, hoặc kích hoạt tab đã có.
    /// - Tab đã tồn tại: kích hoạt (đẩy lên đầu MRU), trả về tab đó.
    /// - Tab mới: chèn ngay sau ActiveTab hiện tại (hoặc cuối nếu không có), kích hoạt, trả về.
    /// </summary>
    internal DocumentTab OpenOrActivate(string documentId, string title, string path, int? initialPage = null)
    {
        // Kiểm tra tab đã tồn tại theo documentId.
        foreach (var existing in _tabItems)
        {
            if (existing is DocumentTab doc && doc.DocumentId == documentId)
            {
                // Đặt trang TRƯỚC khi kích hoạt để viewer (per-tab) nạp thẳng tại trang đích.
                if (initialPage is int pe) doc.Page = pe;
                Activate(doc);
                return doc;
            }
        }

        // Tạo tab mới và chèn ngay sau ActiveTab. Đặt trang TRƯỚC khi chèn/kích hoạt
        // để viewer mở thẳng tại trang đích (cross-doc jump), tránh cuộn-sau-khi-nạp không ổn định.
        var tab = new DocumentTab(documentId, title, path);
        if (initialPage is int ip) tab.Page = ip;
        int insertAt = _activeTab is null ? _tabItems.Count : _tabItems.IndexOf(_activeTab) + 1;
        _tabItems.Insert(insertAt, tab);
        Activate(tab);
        return tab;
    }

    /// <summary>
    /// Đóng <paramref name="tab"/>: gỡ khỏi Tabs và MRU.
    /// Nếu là ActiveTab, kích hoạt tab MRU tiếp theo (hoặc null nếu không còn).
    /// </summary>
    internal void Close(OpenTab tab)
    {
        bool wasActive = _activeTab == tab;
        _tabItems.Remove(tab);
        _mru.Remove(tab);

        if (wasActive)
        {
            // Kích hoạt tab được dùng gần nhất trong số còn lại.
            var next = _mru.Count > 0 ? _mru[0] : null;
            // Gán trực tiếp để tránh tự thêm lại vào MRU (Activate đã làm điều đó).
            if (next is not null)
                Activate(next);
            else
                ActiveTab = null;
        }

        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>
    /// Kích hoạt tab đã tồn tại trong Open Set (dùng cho ActivateTabCommand).
    /// Không làm gì nếu tab không nằm trong Tabs.
    /// </summary>
    internal void ActivateTab(OpenTab tab)
    {
        if (!_tabItems.Contains(tab)) return;
        Activate(tab);
    }

    /// <summary>
    /// Dựng lại Open Set từ trạng thái đã lưu. Thêm mọi tab theo thứ tự nhưng KHÔNG hydrate;
    /// chỉ kích hoạt tab active được chỉ định -> đúng một ActiveTabChanged -> đúng một lần nạp Document.
    /// Dùng khi vào lại Workspace. Gọi khi Open Set đang rỗng.
    /// </summary>
    internal void RestoreTabs(IReadOnlyList<OpenTab> tabs, string? activeDocumentId)
    {
        foreach (var tab in tabs)
        {
            _tabItems.Add(tab);
            _mru.Add(tab);   // MRU tạm theo thứ tự khôi phục; Activate sẽ đẩy tab active lên đầu
        }
        OnPropertyChanged(nameof(HasTabs));

        var tabToActivate = activeDocumentId is null
            ? null
            : _tabItems.FirstOrDefault(t => t.ItemId == activeDocumentId);
        if (tabToActivate is not null)
            Activate(tabToActivate);
    }

    /// <summary>
    /// Xoá toàn bộ Open Set (tabs + MRU + tab active) khi rời/đổi Workspace,
    /// để khôi phục/seed bắt đầu từ trạng thái rỗng — không tích lũy, không trùng tab.
    /// </summary>
    internal void Reset()
    {
        _mru.Clear();
        _tabItems.Clear();
        ActiveTab = null;   // ActiveTabChanged(null): không hydrate
        OnPropertyChanged(nameof(HasTabs));
    }

    // Kích hoạt tab: cập nhật ActiveTab và đẩy lên đầu MRU.
    // Nạp tab TRƯỚC khi đổi ActiveTab để handler ActiveTabChanged thấy tab đã ở trạng thái nạp.
    private void Activate(OpenTab tab)
    {
        _mru.Remove(tab);
        _mru.Insert(0, tab);
        if (tab is DocumentTab documentTab) documentTab.IsHydrated = true;
        EvictBeyondWarmLimit();
        ActiveTab = tab;
    }

    // Tab ngoài nhóm ấm (active + N MRU) nhả handle PDFium; tab chưa từng kích hoạt (khôi phục lười) vốn chưa nạp.
    private void EvictBeyondWarmLimit()
    {
        for (int i = _maxWarmInactiveTabs + 1; i < _mru.Count; i++)
            if (_mru[i] is DocumentTab documentTab) documentTab.IsHydrated = false;
    }
}
