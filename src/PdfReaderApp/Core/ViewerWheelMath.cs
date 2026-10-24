namespace PdfReaderApp.Core;

/// <summary>Trạng thái cuộn của viewer tại thời điểm lăn chuột (đầu vào cho ResolvePageChange).</summary>
public sealed record WheelPageContext(
    int Delta, bool AtTop, bool AtBottom, int CurrentPage, int TotalPages, bool IsFacing, bool ShowCover);

/// <summary>Kết quả lăn chuột ở chế độ một trang / hai trang: có nuốt sự kiện không, trang mới, có cuộn về đầu không.</summary>
public sealed record WheelPageDecision(bool Handled, int? NewPage, bool ScrollToTop);

/// <summary>Điểm neo (toạ độ chuột trong ScrollViewer) cùng offset cuộn hiện tại khi zoom bằng Ctrl+lăn.</summary>
public sealed record WheelZoomAnchor(double HorizontalOffset, double VerticalOffset, double MouseX, double MouseY);

/// <summary>
/// Logic thuần (tách khỏi code-behind để test) cho thao tác lăn chuột trên PdfViewerControl:
/// zoom bám con trỏ và chuyển trang khi đã chạm biên.
/// </summary>
public static class ViewerWheelMath
{
    private const double ZoomStep = 0.1;
    private const double MinZoom = 0.1;
    private const double MaxZoom = 5.0;
    // Dưới ngưỡng này coi như đã chạm giới hạn zoom, không đổi layout.
    private const double ZoomEpsilon = 0.01;

    /// <summary>Zoom mới sau một nấc lăn, hoặc null nếu đã ở giới hạn (không nhảy layout).</summary>
    public static double? NextZoom(double oldZoom, int wheelDelta)
    {
        double newZoom = oldZoom + (wheelDelta > 0 ? ZoomStep : -ZoomStep);
        newZoom = Math.Max(MinZoom, Math.Min(MaxZoom, newZoom));
        return Math.Abs(newZoom - oldZoom) < ZoomEpsilon ? null : newZoom;
    }

    /// <summary>Offset cuộn mới để điểm dưới con trỏ đứng yên sau khi đổi zoom.</summary>
    public static (double HorizontalOffset, double VerticalOffset) AnchoredOffsets(
        WheelZoomAnchor anchor, double oldZoom, double newZoom)
    {
        double relX = (anchor.HorizontalOffset + anchor.MouseX) / oldZoom;
        double relY = (anchor.VerticalOffset + anchor.MouseY) / oldZoom;
        return (relX * newZoom - anchor.MouseX, relY * newZoom - anchor.MouseY);
    }

    /// <summary>
    /// Chế độ một trang / hai trang: đơn vị hiện tại lấp đầy khung, nên lăn trong nó trước và chỉ sang
    /// đơn vị kế/trước khi đã ở biên.
    /// </summary>
    public static WheelPageDecision ResolvePageChange(WheelPageContext ctx)
    {
        if (ctx.Delta < 0 && ctx.AtBottom && ctx.CurrentPage < ctx.TotalPages)
        {
            int next = ctx.IsFacing
                ? Math.Clamp(PageLayoutCalculator.AdjacentFacingUnitFirstPage(ctx.ShowCover, ctx.TotalPages, ctx.CurrentPage - 1, forward: true) + 1, 1, ctx.TotalPages)
                : Math.Min(ctx.TotalPages, ctx.CurrentPage + 1);
            return new WheelPageDecision(true, next, true);
        }
        if (ctx.Delta > 0 && ctx.AtTop && ctx.CurrentPage > 1)
        {
            int previous = ctx.IsFacing
                ? Math.Clamp(PageLayoutCalculator.AdjacentFacingUnitFirstPage(ctx.ShowCover, ctx.TotalPages, ctx.CurrentPage - 1, forward: false) + 1, 1, ctx.TotalPages)
                : Math.Max(1, ctx.CurrentPage - 1);
            return new WheelPageDecision(true, previous, false);
        }
        return new WheelPageDecision(false, null, false);
    }
}
