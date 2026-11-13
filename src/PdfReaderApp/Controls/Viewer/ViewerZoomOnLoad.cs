namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Quyết định có fit trang đầu vừa khung khi nạp tài liệu hay không (tách để test, TEST-2).
/// Giữ zoom của tab khi nạp lại đúng tài liệu vừa bị evict (#48) hoặc khi tab khôi phục từ lần trước mang zoom đã lưu
/// (#263); mọi lần nạp khác (tab mới mở, đọc lẻ) đều fit.
/// </summary>
internal sealed class ViewerZoomOnLoad
{
    private string? _evictedPath;

    public void OnEvicted(string path) => _evictedPath = path;

    /// <summary>
    /// Trả về true nếu phải fit; tiêu thụ dấu evict nên lần nạp sau quay về hành vi thường.
    /// <paramref name="hasSavedZoom"/>: tab mang zoom đã lưu từ lần trước nên không bao giờ fit đè.
    /// </summary>
    public bool ShouldFitOnLoad(string path, bool hasSavedZoom)
    {
        bool isRehydrate = string.Equals(path, _evictedPath, StringComparison.OrdinalIgnoreCase);
        _evictedPath = null;
        return !isRehydrate && !hasSavedZoom;
    }
}
