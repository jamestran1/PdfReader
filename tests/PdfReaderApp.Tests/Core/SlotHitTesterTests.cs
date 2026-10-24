using System.Windows;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class SlotHitTesterTests
{
    private static readonly PageSlot[] Continuous =
    [
        new(0, 10, 10, 100, 200),
        new(1, 10, 220, 100, 200),
    ];

    private static readonly PageSlot[] TwoPage =
    [
        new(0, 0, 0, 100, 150),
        new(1, 110, 0, 100, 150),
    ];

    [Fact]
    public void AtPoint_PointInsideContinuousPage_ReturnsThatPage()
        => Assert.Equal(1, SlotHitTester.AtPoint(Continuous, new Point(60, 320))!.PageIndex);

    [Fact]
    public void AtPoint_PointInGutter_ReturnsNull()
        => Assert.Null(SlotHitTester.AtPoint(Continuous, new Point(60, 215)));

    [Fact]
    public void AtPoint_PointOnEdge_CountsAsHit()
    {
        Assert.NotNull(SlotHitTester.AtPoint(Continuous, new Point(110, 210)));
        Assert.NotNull(SlotHitTester.AtPoint(Continuous, new Point(10, 10)));
    }

    [Fact]
    public void AtPoint_TwoPageLayout_PicksPageBySideOfGap()
    {
        Assert.Equal(0, SlotHitTester.AtPoint(TwoPage, new Point(50, 20))!.PageIndex);
        Assert.Equal(1, SlotHitTester.AtPoint(TwoPage, new Point(150, 20))!.PageIndex);
        Assert.Null(SlotHitTester.AtPoint(TwoPage, new Point(105, 20)));
    }

    [Fact]
    public void AtPoint_NoSlots_ReturnsNull()
        => Assert.Null(SlotHitTester.AtPoint([], new Point(5, 5)));

    [Fact]
    public void ToPdfPoint_DividesOffsetFromPageOriginByZoom()
    {
        var pdf = SlotHitTester.ToPdfPoint(Continuous[1], new Point(60, 320), 2.0);
        Assert.Equal(25, pdf.X, 6);
        Assert.Equal(50, pdf.Y, 6);
    }

    [Fact]
    public void AtOffset_InsidePage_ReturnsThatPage()
        => Assert.Equal(1, SlotHitTester.AtOffset(Continuous, 300)!.PageIndex);

    [Fact]
    public void AtOffset_OnSharedEdgeOrGutter_FollowsFirstMatchRule()
    {
        Assert.Equal(0, SlotHitTester.AtOffset(Continuous, 210)!.PageIndex);
        Assert.Null(SlotHitTester.AtOffset(Continuous, 215));
        Assert.Null(SlotHitTester.AtOffset(Continuous, 5));
    }

    [Fact]
    public void CaptureAnchor_MidPage_ReturnsSlotAndFraction()
    {
        int index = SlotHitTester.CaptureAnchor(Continuous, 320, out double fraction);
        Assert.Equal(1, index);
        Assert.Equal(0.5, fraction, 6);
    }

    [Fact]
    public void CaptureAnchor_InGutter_AnchorsToNextPageWithClampedFraction()
    {
        int index = SlotHitTester.CaptureAnchor(Continuous, 215, out double fraction);
        Assert.Equal(1, index);
        Assert.Equal(0, fraction, 6);
    }

    [Fact]
    public void CaptureAnchor_BelowLastPage_AnchorsToLastPageWithFractionOne()
    {
        int index = SlotHitTester.CaptureAnchor(Continuous, 9999, out double fraction);
        Assert.Equal(1, index);
        Assert.Equal(1, fraction, 6);
    }

    [Fact]
    public void CaptureAnchor_NoSlots_ReturnsFirstSlotZero()
    {
        int index = SlotHitTester.CaptureAnchor([], 100, out double fraction);
        Assert.Equal(0, index);
        Assert.Equal(0, fraction);
    }

    [Fact]
    public void TryRestoreCenterY_AfterRelayout_KeepsSameFractionOfNewHeight()
    {
        PageSlot[] bigger = [new(0, 10, 10, 200, 400), new(1, 10, 420, 200, 400)];
        Assert.True(SlotHitTester.TryRestoreCenterY(bigger, 1, 0.5, out double centerY));
        Assert.Equal(620, centerY, 6);
    }

    [Fact]
    public void TryRestoreCenterY_SlotGone_ReturnsFalse()
        => Assert.False(SlotHitTester.TryRestoreCenterY(Continuous, 5, 0.5, out _));
}
