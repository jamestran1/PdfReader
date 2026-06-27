namespace PdfReaderApp.Core;

/// <summary>
/// Logic thuần (tách để test hồi quy) quyết định khi nào PdfViewerControl được đồng bộ scroll-&gt;trang.
/// </summary>
public static class ViewerScrollGate
{
    /// <summary>
    /// Chỉ đồng bộ scroll-&gt;trang khi viewer HIỂN THỊ thật và có viewport &gt; 0.
    /// Viewer ẩn (tab không active, hoặc đã rời Workspace nên host bị Collapsed) khiến ScrollViewer reset
    /// offset/viewport về 0 và phát ScrollChanged; nếu đồng bộ lúc đó, trang giữa khung = trang 1 sẽ bị ghi
    /// ngược vào OpenTab.Page (binding TwoWay) và lưu đè trang đã lưu về cover — regression "tab active mở ở
    /// cover sau khi rời Workspace". Chặn đồng bộ khi ẩn để giữ nguyên trang đã lưu.
    /// </summary>
    public static bool ShouldSyncPageFromScroll(bool isVisible, double viewportHeight)
        => isVisible && viewportHeight > 0;
}
