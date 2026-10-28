namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Quyết định có fit trang đầu vừa khung khi nạp tài liệu hay không (tách để test, TEST-2).
/// Nạp lại đúng tài liệu vừa bị evict (#48) thì giữ zoom của tab; mọi lần nạp khác đều fit.
/// </summary>
internal sealed class ViewerZoomOnLoad
{
    private string? _evictedPath;

    public void OnEvicted(string path) => _evictedPath = path;

    /// <summary>Trả về true nếu phải fit; tiêu thụ dấu evict nên lần nạp sau quay về hành vi thường.</summary>
    public bool ShouldFitOnLoad(string path)
    {
        bool isRehydrate = string.Equals(path, _evictedPath, StringComparison.OrdinalIgnoreCase);
        _evictedPath = null;
        return !isRehydrate;
    }
}
