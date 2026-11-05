using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Quản lý Open Set: tập các Tab đang mở trong phiên Workspace có tên. Đóng tab, MRU và khôi phục ở <see cref="TabHost"/>;
/// ở đây thêm Tab Document và việc hydrate/evict handle PDFium theo MRU (ADR 0003).
/// Không phụ thuộc PDFium hay bất kỳ dịch vụ document nào, thuần logic tab.
/// </summary>
public sealed class TabSetViewModel : TabHost
{
    /// <summary>Số tab inactive gần nhất (theo MRU) vẫn giữ handle PDFium, ngoài tab active (ADR 0003).</summary>
    public const int DefaultMaxWarmInactiveTabs = 5;

    private readonly int _maxWarmInactiveTabs;

    public TabSetViewModel(int maxWarmInactiveTabs = DefaultMaxWarmInactiveTabs, TimeProvider? clock = null)
        : base(clock)
    {
        _maxWarmInactiveTabs = maxWarmInactiveTabs;
    }

    /// <summary>
    /// Mở tab cho <paramref name="documentId"/>, hoặc kích hoạt tab đã có.
    /// - Tab đã tồn tại: kích hoạt (đẩy lên đầu MRU), trả về tab đó.
    /// - Tab mới: chèn ngay sau ActiveTab hiện tại (hoặc cuối nếu không có), kích hoạt, trả về.
    /// </summary>
    internal DocumentTab OpenOrActivate(string documentId, string title, string path, int? initialPage = null)
    {
        // Kiểm tra tab đã tồn tại theo documentId.
        foreach (var existing in Items)
        {
            if (existing is DocumentTab doc && doc.DocumentId == documentId)
            {
                // Đặt trang TRƯỚC khi kích hoạt để viewer (per-tab) nạp thẳng tại trang đích.
                if (initialPage is int pe) doc.Page = pe;
                ActivateTab(doc);
                return doc;
            }
        }

        // Tạo tab mới và chèn ngay sau ActiveTab. Đặt trang TRƯỚC khi chèn/kích hoạt
        // để viewer mở thẳng tại trang đích (cross-doc jump), tránh cuộn-sau-khi-nạp không ổn định.
        var tab = new DocumentTab(documentId, title, path);
        if (initialPage is int ip) tab.Page = ip;
        InsertAfterActiveAndActivate(tab);
        return tab;
    }

    protected override void OnActivating(OpenTab tab)
    {
        if (tab is DocumentTab documentTab) documentTab.IsHydrated = true;
        EvictBeyondWarmLimit();
    }

    // Tab Document ngoài nhóm ấm (N + 1 tab gần nhất) nhả handle PDFium; tab chưa từng kích hoạt (khôi phục lười) vốn chưa nạp.
    // Tab Bản soạn không giữ PDFium nên không chiếm chỗ trong nhóm ấm (ADR 0009).
    private void EvictBeyondWarmLimit()
    {
        int documentsSeen = 0;
        foreach (var tab in MostRecentFirst)
        {
            if (tab is not DocumentTab documentTab) continue;
            if (++documentsSeen > _maxWarmInactiveTabs + 1) documentTab.IsHydrated = false;
        }
    }
}
