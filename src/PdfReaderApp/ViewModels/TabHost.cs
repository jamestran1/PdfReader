using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Nguồn tab của một dải tab: danh sách theo thứ tự hiển thị, tab active, MRU, đóng, kích hoạt, khôi phục (ADR 0009).
/// Workspace có tên dùng <see cref="TabSetViewModel"/>, Default Workspace dùng <see cref="LightTabStripViewModel"/>;
/// hai bên chia sẻ lớp này để đóng tab, chuyển sang tab MRU và khôi phục không lệch nhau.
/// </summary>
public abstract class TabHost : ObservableObject
{
    private readonly ObservableCollection<OpenTab> _tabItems = [];

    // Danh sách MRU (most-recently-activated): phần tử đầu = được kích hoạt gần nhất.
    private readonly List<OpenTab> _mru = [];

    private OpenTab? _activeTab;

    protected TabHost()
    {
        Tabs = new ReadOnlyObservableCollection<OpenTab>(_tabItems);
        _tabItems.CollectionChanged += (_, _) => TabsChanged?.Invoke();
    }

    /// <summary>Danh sách tab theo thứ tự hiển thị (Tab Strip order). Chỉ đọc: mọi thay đổi đi qua Phiên đọc.</summary>
    public ReadOnlyObservableCollection<OpenTab> Tabs { get; }

    /// <summary>Tab đang kích hoạt. null nếu không có tab.</summary>
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

    /// <summary>Các tab theo thứ tự MRU, gần nhất trước. Lớp con dùng để quyết định tab nào còn giữ tài nguyên.</summary>
    protected IReadOnlyList<OpenTab> MostRecentFirst => _mru;

    /// <summary>Các tab theo thứ tự hiển thị, cho lớp con tìm tab đã có.</summary>
    protected IReadOnlyList<OpenTab> Items => _tabItems;

    /// <summary>Tab Bản soạn đang mở của <paramref name="compositionId"/>, null khi chưa mở.</summary>
    internal CompositionTab? FindComposition(string compositionId)
        => _tabItems.OfType<CompositionTab>().FirstOrDefault(t => t.CompositionId == compositionId);

    /// <summary>
    /// Mở Tab Bản soạn cho <paramref name="composition"/>, hoặc kích hoạt tab đã có (không mở tab trùng).
    /// Tab mới chèn ngay sau ActiveTab, như Tab Document; khi có <paramref name="afterCompositionId"/> và tab đó còn mở thì
    /// chèn ngay sau tab đó (Sinh lại: bản mới nằm bên phải bản cũ).
    /// </summary>
    internal CompositionTab OpenOrActivateComposition(Composition composition, string? afterCompositionId = null)
    {
        if (FindComposition(composition.Id) is { } existing)
        {
            Activate(existing);
            return existing;
        }

        var tab = new CompositionTab(composition);
        var anchor = (afterCompositionId is null ? null : FindComposition(afterCompositionId)) ?? _activeTab;
        int insertAt = anchor is null ? _tabItems.Count : _tabItems.IndexOf(anchor) + 1;
        _tabItems.Insert(insertAt, tab);
        Activate(tab);
        return tab;
    }

    /// <summary>
    /// Đóng <paramref name="tab"/>: gỡ khỏi Tabs và MRU. Tab không đóng được (<see cref="OpenTab.CanClose"/>) thì không làm gì.
    /// Nếu là ActiveTab, kích hoạt tab MRU tiếp theo (hoặc null nếu không còn).
    /// </summary>
    internal void Close(OpenTab tab)
    {
        if (!tab.CanClose) return;
        bool wasActive = _activeTab == tab;
        _tabItems.Remove(tab);
        _mru.Remove(tab);

        if (wasActive)
        {
            // Kích hoạt tab được dùng gần nhất trong số còn lại.
            var next = _mru.Count > 0 ? _mru[0] : null;
            if (next is not null)
                Activate(next);
            else
                ActiveTab = null;
        }

        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>
    /// Kích hoạt tab đã tồn tại (dùng cho ActivateTabCommand). Không làm gì nếu tab không nằm trong Tabs.
    /// </summary>
    internal void ActivateTab(OpenTab tab)
    {
        if (!_tabItems.Contains(tab)) return;
        Activate(tab);
    }

    /// <summary>
    /// Dựng lại tập tab từ trạng thái đã lưu. Thêm mọi tab theo thứ tự nhưng KHÔNG hydrate;
    /// chỉ kích hoạt tab active được chỉ định -> đúng một ActiveTabChanged -> đúng một lần nạp Document.
    /// Dùng khi vào lại Workspace. Gọi khi tập tab đang rỗng.
    /// </summary>
    internal void RestoreTabs(IReadOnlyList<OpenTab> tabs, string? activeItemId)
    {
        foreach (var tab in tabs)
        {
            _tabItems.Add(tab);
            _mru.Add(tab);   // MRU tạm theo thứ tự khôi phục; Activate sẽ đẩy tab active lên đầu
        }
        OnPropertyChanged(nameof(HasTabs));

        var tabToActivate = activeItemId is null
            ? null
            : _tabItems.FirstOrDefault(t => t.ItemId == activeItemId);
        if (tabToActivate is not null)
            Activate(tabToActivate);
    }

    /// <summary>
    /// Xoá toàn bộ tab (tabs + MRU + tab active) khi rời/đổi Workspace,
    /// để khôi phục/seed bắt đầu từ trạng thái rỗng, không tích lũy, không trùng tab.
    /// </summary>
    internal void Reset()
    {
        _mru.Clear();
        _tabItems.Clear();
        ActiveTab = null;   // ActiveTabChanged(null): không hydrate
        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>Thêm <paramref name="tab"/> vào vị trí <paramref name="index"/> và kích hoạt nó.</summary>
    protected void InsertAndActivate(int index, OpenTab tab)
    {
        _tabItems.Insert(index, tab);
        Activate(tab);
    }

    /// <summary>Tab được chọn vào dòng MRU và sắp thành ActiveTab. Lớp con chặn ở đây để nạp tab trước khi đổi ActiveTab.</summary>
    protected virtual void OnActivating(OpenTab tab)
    {
    }

    /// <summary>Thêm <paramref name="tab"/> ngay sau ActiveTab (cuối nếu chưa có) và kích hoạt nó.</summary>
    protected void InsertAfterActiveAndActivate(OpenTab tab)
        => InsertAndActivate(_activeTab is null ? _tabItems.Count : _tabItems.IndexOf(_activeTab) + 1, tab);

    // Kích hoạt tab: cập nhật ActiveTab và đẩy lên đầu MRU.
    // Nạp tab TRƯỚC khi đổi ActiveTab để handler ActiveTabChanged thấy tab đã ở trạng thái nạp.
    private void Activate(OpenTab tab)
    {
        _mru.Remove(tab);
        _mru.Insert(0, tab);
        OnActivating(tab);
        ActiveTab = tab;
    }
}
