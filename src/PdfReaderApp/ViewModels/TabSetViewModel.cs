using System;
using System.Collections.Generic;
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
    // Danh sách tab theo thứ tự hiển thị (Tab Strip order).
    public ObservableCollection<OpenTab> Tabs { get; } = new();

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
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>Phát khi ActiveTab thay đổi (kể cả khi thành null).</summary>
    public event Action<OpenTab?>? ActiveTabChanged;

    // Danh sách MRU (most-recently-activated): phần tử đầu = được kích hoạt gần nhất.
    private readonly List<OpenTab> _mru = new();

    /// <summary>
    /// Mở tab cho <paramref name="documentId"/>, hoặc kích hoạt tab đã có.
    /// - Tab đã tồn tại: kích hoạt (đẩy lên đầu MRU), trả về tab đó.
    /// - Tab mới: chèn ngay sau ActiveTab hiện tại (hoặc cuối nếu không có), kích hoạt, trả về.
    /// </summary>
    public OpenTab OpenOrActivate(string documentId, string title, string path, int? initialPage = null)
    {
        // Kiểm tra tab đã tồn tại theo documentId.
        foreach (var existing in Tabs)
        {
            if (existing.DocumentId == documentId)
            {
                // Đặt trang TRƯỚC khi kích hoạt để viewer (per-tab) nạp thẳng tại trang đích.
                if (initialPage is int pe) existing.Page = pe;
                Activate(existing);
                return existing;
            }
        }

        // Tạo tab mới và chèn ngay sau ActiveTab. Đặt trang TRƯỚC khi chèn/kích hoạt
        // để viewer mở thẳng tại trang đích (cross-doc jump), tránh cuộn-sau-khi-nạp không ổn định.
        var tab = new OpenTab(documentId, title, path);
        if (initialPage is int ip) tab.Page = ip;
        int insertAt = _activeTab is null ? Tabs.Count : Tabs.IndexOf(_activeTab) + 1;
        Tabs.Insert(insertAt, tab);
        Activate(tab);
        return tab;
    }

    /// <summary>
    /// Đóng <paramref name="tab"/>: gỡ khỏi Tabs và MRU.
    /// Nếu là ActiveTab, kích hoạt tab MRU tiếp theo (hoặc null nếu không còn).
    /// </summary>
    public void Close(OpenTab tab)
    {
        bool wasActive = _activeTab == tab;
        Tabs.Remove(tab);
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
    public void ActivateTab(OpenTab tab)
    {
        if (!Tabs.Contains(tab)) return;
        Activate(tab);
    }

    // Kích hoạt tab: cập nhật ActiveTab và đẩy lên đầu MRU.
    private void Activate(OpenTab tab)
    {
        _mru.Remove(tab);
        _mru.Insert(0, tab);
        ActiveTab = tab;
    }
}
