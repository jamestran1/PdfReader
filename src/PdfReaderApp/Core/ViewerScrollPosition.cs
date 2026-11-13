namespace PdfReaderApp.Core;

/// <summary>
/// Đổi qua lại giữa offset cuộn của viewer và <c>OpenTab.ScrollNorm</c> (phần đã cuộn trong trang hiện tại, 0..1).
/// Chuẩn hoá theo chiều cao slot nên giá trị lưu không phụ thuộc zoom; chỉ offset tuyệt đối mới phụ thuộc,
/// vì vậy phải tính offset sau khi zoom đã ổn định.
/// </summary>
public static class ViewerScrollPosition
{
    /// <summary>Phần của slot đã cuộn qua tại <paramref name="offset"/>, kẹp vào [0..1].</summary>
    public static double ToScrollNorm(double offset, double slotY, double slotHeight)
    {
        if (slotHeight <= 0 || !double.IsFinite(offset)) return 0;
        return Math.Clamp((offset - slotY) / slotHeight, 0, 1);
    }

    /// <summary>Offset cuộn tương ứng với <paramref name="scrollNorm"/> trong slot; giá trị ngoài [0..1] hoặc không hợp lệ bị kẹp.</summary>
    public static double ToOffset(double slotY, double slotHeight, double scrollNorm)
    {
        double norm = double.IsFinite(scrollNorm) ? Math.Clamp(scrollNorm, 0, 1) : 0;
        return slotY + norm * Math.Max(slotHeight, 0);
    }

    /// <summary>
    /// Có ghi offset hiện tại vào <c>ScrollNorm</c> hay không. Không ghi khi viewer ẩn (ScrollViewer reset offset về 0,
    /// xem <see cref="ViewerScrollGate"/>), khi đang chờ cuộn tới đích sau nạp (offset trung gian), hay ngoài chế độ cuộn liên tục.
    /// </summary>
    public static bool ShouldPersist(bool isVisible, double viewportHeight, bool isSettlePending, bool isContinuous)
        => isContinuous && !isSettlePending && ViewerScrollGate.ShouldSyncPageFromScroll(isVisible, viewportHeight);
}
