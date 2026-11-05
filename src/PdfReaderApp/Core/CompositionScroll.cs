namespace PdfReaderApp.Core;

/// <summary>Đổi qua lại giữa offset cuộn của Tab Bản soạn và vị trí chuẩn hoá [0..1] lưu trong <c>scroll_norm</c>.</summary>
public static class CompositionScroll
{
    public static double ToNormalized(double offset, double scrollableHeight)
        => scrollableHeight <= 0 ? 0 : Math.Clamp(offset / scrollableHeight, 0, 1);

    public static double ToOffset(double normalized, double scrollableHeight)
        => scrollableHeight <= 0 ? 0 : Math.Clamp(normalized, 0, 1) * scrollableHeight;

    /// <summary>
    /// Có ghi offset hiện tại vào <c>ScrollNorm</c> hay không. View ẩn (tab không active) khiến ScrollViewer reset
    /// offset về 0 và phát ScrollChanged; ghi lúc đó sẽ lưu đè vị trí đã lưu bằng 0 (cùng bẫy với ViewerScrollGate).
    /// </summary>
    public static bool ShouldPersist(bool restoring, bool isVisible, double viewportHeight, double verticalChange)
        => !restoring && verticalChange != 0 && ViewerScrollGate.ShouldSyncPageFromScroll(isVisible, viewportHeight);
}
