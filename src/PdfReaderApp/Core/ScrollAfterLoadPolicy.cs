namespace PdfReaderApp.Core;

/// <summary>
/// Quyết định thuần: sau khi nạp tài liệu có cần cuộn tới trang đích không. Chỉ xét số trang thì Đoạn nguồn
/// giữa trang 1 bị bỏ sót, vì đích trên trang 1 không còn là offset 0.
/// </summary>
public static class ScrollAfterLoadPolicy
{
    /// <param name="isContinuous">Chế độ xem cuộn liên tục.</param>
    /// <param name="currentPage">Trang đích (1-based).</param>
    /// <param name="targetOffset">Offset đích của trang, hoặc null khi slot chưa dựng.</param>
    /// <param name="pageTop">Y của slot trang đích, hoặc null khi slot chưa dựng.</param>
    public static bool NeedsScroll(bool isContinuous, int currentPage, double? targetOffset, double? pageTop = null) =>
        isContinuous && (currentPage > 1 || targetOffset > (pageTop ?? 0));
}
