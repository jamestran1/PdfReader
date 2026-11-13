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

    /// <summary>
    /// Có phải bật guard chờ cuộn TRƯỚC khi layout (slot chưa dựng nên chưa biết offset đích) hay không.
    /// Ngoài trang sau trang 1, trang 1 có <paramref name="scrollNorm"/> dương cũng cuộn xuống giữa trang.
    /// </summary>
    public static bool NeedsGuardBeforeLayout(bool isContinuous, int currentPage, double scrollNorm) =>
        NeedsScroll(isContinuous, currentPage, targetOffset: null) || (isContinuous && scrollNorm > 0);
}
