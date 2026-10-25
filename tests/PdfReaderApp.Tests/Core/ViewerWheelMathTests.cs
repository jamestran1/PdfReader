using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class ViewerWheelMathTests
{
    [Fact]
    public void NextZoom_WheelUp_AddsOneStep()
        => Assert.Equal(1.1, ViewerWheelMath.NextZoom(1.0, 120)!.Value, 6);

    [Fact]
    public void NextZoom_WheelDown_SubtractsOneStep()
        => Assert.Equal(0.9, ViewerWheelMath.NextZoom(1.0, -120)!.Value, 6);

    [Fact]
    public void NextZoom_AtMaxLimit_ReturnsNull()
        => Assert.Null(ViewerWheelMath.NextZoom(5.0, 120));

    [Fact]
    public void NextZoom_AtMinLimit_ReturnsNull()
        => Assert.Null(ViewerWheelMath.NextZoom(0.1, -120));

    [Fact]
    public void NextZoom_NearMaxLimit_ClampsToMax()
        => Assert.Equal(5.0, ViewerWheelMath.NextZoom(4.95, 120)!.Value, 6);

    [Fact]
    public void AnchoredOffsets_KeepsPointUnderCursorStill()
    {
        var anchor = new WheelZoomAnchor(HorizontalOffset: 100, VerticalOffset: 200, MouseX: 50, MouseY: 60);
        var (h, v) = ViewerWheelMath.AnchoredOffsets(anchor, oldZoom: 1.0, newZoom: 2.0);
        Assert.Equal(250, h, 6);
        Assert.Equal(460, v, 6);
    }

    private static WheelPageContext Ctx(int delta, bool atTop = false, bool atBottom = false, int page = 5,
        int total = 10, bool facing = false, bool cover = false)
        => new(delta, atTop, atBottom, page, total, facing, cover);

    [Fact]
    public void ResolvePageChange_DownAtBottom_AdvancesAndScrollsToTop()
        => Assert.Equal(new WheelPageDecision(Handled: true, NewPage: 6, ScrollToTop: true), ViewerWheelMath.ResolvePageChange(Ctx(-120, atBottom: true)));

    [Fact]
    public void ResolvePageChange_DownNotAtBottom_NotHandled()
        => Assert.Equal(new WheelPageDecision(Handled: false, NewPage: null, ScrollToTop: false), ViewerWheelMath.ResolvePageChange(Ctx(-120)));

    [Fact]
    public void ResolvePageChange_DownAtBottomOnLastPage_NotHandled()
        => Assert.False(ViewerWheelMath.ResolvePageChange(Ctx(-120, atBottom: true, page: 10)).Handled);

    [Fact]
    public void ResolvePageChange_UpAtTop_GoesBackWithoutScrollToTop()
        => Assert.Equal(new WheelPageDecision(Handled: true, NewPage: 4, ScrollToTop: false), ViewerWheelMath.ResolvePageChange(Ctx(120, atTop: true)));

    [Fact]
    public void ResolvePageChange_UpAtTopOnFirstPage_NotHandled()
        => Assert.False(ViewerWheelMath.ResolvePageChange(Ctx(120, atTop: true, page: 1)).Handled);

    [Fact]
    public void ResolvePageChange_FacingDownAtBottom_JumpsToNextUnitFirstPage()
    {
        // 10 trang, không bìa: đơn vị (1,2)(3,4)(5,6)... đang ở trang 3 thì sang trang 5.
        var d = ViewerWheelMath.ResolvePageChange(Ctx(-120, atBottom: true, page: 3, facing: true));
        Assert.Equal(new WheelPageDecision(Handled: true, NewPage: 5, ScrollToTop: true), d);
    }

    [Fact]
    public void ResolvePageChange_FacingWithCoverDownAtBottom_JumpsToNextUnitFirstPage()
    {
        // Có bìa: đơn vị (1)(2,3)(4,5)... đang ở trang 2 thì lăn xuống sang trang 4.
        var d = ViewerWheelMath.ResolvePageChange(Ctx(-120, atBottom: true, page: 2, facing: true, cover: true));
        Assert.Equal(new WheelPageDecision(Handled: true, NewPage: 4, ScrollToTop: true), d);
    }

    [Fact]
    public void ResolvePageChange_FacingWithCoverUpAtTop_GoesBackToCoverPage()
    {
        var d = ViewerWheelMath.ResolvePageChange(Ctx(120, atTop: true, page: 2, facing: true, cover: true));
        Assert.Equal(new WheelPageDecision(Handled: true, NewPage: 1, ScrollToTop: false), d);
    }

    [Fact]
    public void ResolvePageChange_FacingUpAtTop_JumpsToPreviousUnitFirstPage()
    {
        var d = ViewerWheelMath.ResolvePageChange(Ctx(120, atTop: true, page: 5, facing: true));
        Assert.Equal(new WheelPageDecision(Handled: true, NewPage: 3, ScrollToTop: false), d);
    }

    // Characterization (#209): ghim hành vi con lăn mà PdfViewerControl đang dựa vào trước khi tách ViewerWheelInput.

    [Fact]
    public void NextZoom_ZeroDelta_ZoomsOut()
        => Assert.Equal(0.9, ViewerWheelMath.NextZoom(1.0, 0)!.Value, 6);

    [Fact]
    public void NextZoom_NearMinLimit_ClampsToMin()
        => Assert.Equal(0.1, ViewerWheelMath.NextZoom(0.15, -120)!.Value, 6);

    [Fact]
    public void AnchoredOffsets_ZoomOut_KeepsPointUnderCursorStill()
    {
        var anchor = new WheelZoomAnchor(HorizontalOffset: 250, VerticalOffset: 460, MouseX: 50, MouseY: 60);
        var (h, v) = ViewerWheelMath.AnchoredOffsets(anchor, oldZoom: 2.0, newZoom: 1.0);
        Assert.Equal(100, h, 6);
        Assert.Equal(200, v, 6);
    }

    [Fact]
    public void ResolvePageChange_FacingDownAtBottomOnLastPage_NotHandled()
        => Assert.False(ViewerWheelMath.ResolvePageChange(Ctx(-120, atBottom: true, page: 10, facing: true)).Handled);

    [Fact]
    public void ResolvePageChange_UpAtBottomOnly_NotHandled()
        => Assert.False(ViewerWheelMath.ResolvePageChange(Ctx(120, atBottom: true)).Handled);

    [Fact]
    public void ResolvePageChange_DownAtTopOnly_NotHandled()
        => Assert.False(ViewerWheelMath.ResolvePageChange(Ctx(-120, atTop: true)).Handled);

    [Fact]
    public void ResolvePageChange_PageShorterThanViewport_DownAdvancesAndUpGoesBack()
    {
        // Trang thấp hơn khung: vừa ở đỉnh vừa ở đáy, mỗi nấc lăn đổi trang ngay.
        Assert.Equal(6, ViewerWheelMath.ResolvePageChange(Ctx(-120, atTop: true, atBottom: true)).NewPage);
        Assert.Equal(4, ViewerWheelMath.ResolvePageChange(Ctx(120, atTop: true, atBottom: true)).NewPage);
    }
}
