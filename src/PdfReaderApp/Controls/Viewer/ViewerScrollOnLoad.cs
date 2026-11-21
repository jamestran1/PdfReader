namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Quyết định ScrollNorm nào được áp khi nạp tài liệu (tách để test, TEST-2). Chỉ viewer per-tab có binding
/// ScrollNorm mới có giá trị đáng tin (OpenTab lưu và khôi phục). Viewer đọc lẻ không có binding nên giá trị
/// cục bộ là dư lượng của lần cuộn trước, mở lại cùng tệp hay tệp khác đều phải về đầu trang.
/// </summary>
internal static class ViewerScrollOnLoad
{
    public static double NormToApply(bool isScrollNormBound, double scrollNorm)
        => isScrollNormBound && double.IsFinite(scrollNorm) ? scrollNorm : 0;
}
