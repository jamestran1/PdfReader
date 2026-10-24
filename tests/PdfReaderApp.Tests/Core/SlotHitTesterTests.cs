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
    public void TryHit_PointInsideContinuousPage_ReturnsSlotAndPdfPointDividedByScale()
    {
        var hit = SlotHitTester.TryHit(Continuous, 60, 320, 2.0);
        Assert.NotNull(hit);
        Assert.Equal(1, hit.Slot.PageIndex);
        Assert.Equal(25, hit.PdfX, 6);
        Assert.Equal(50, hit.PdfY, 6);
    }

    [Fact]
    public void TryHit_PointInGutter_ReturnsNull()
        => Assert.Null(SlotHitTester.TryHit(Continuous, 60, 215, 1.0));

    [Fact]
    public void TryHit_PointOnEdge_CountsAsHit()
    {
        Assert.NotNull(SlotHitTester.TryHit(Continuous, 110, 210, 1.0));
        Assert.NotNull(SlotHitTester.TryHit(Continuous, 10, 10, 1.0));
    }

    [Fact]
    public void TryHit_TwoPageLayout_PicksPageBySideOfGap()
    {
        Assert.Equal(0, SlotHitTester.TryHit(TwoPage, 50, 20, 1.0)!.Slot.PageIndex);
        Assert.Equal(1, SlotHitTester.TryHit(TwoPage, 150, 20, 1.0)!.Slot.PageIndex);
        Assert.Null(SlotHitTester.TryHit(TwoPage, 105, 20, 1.0));
    }

    [Fact]
    public void TryHit_NoSlots_ReturnsNull()
        => Assert.Null(SlotHitTester.TryHit([], 5, 5, 1.0));

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
        var anchor = SlotHitTester.CaptureAnchor(Continuous, 320);
        Assert.Equal(1, anchor.SlotIndex);
        Assert.Equal(0.5, anchor.Fraction, 6);
    }

    [Fact]
    public void CaptureAnchor_InGutter_AnchorsToNextPageWithClampedFraction()
    {
        var anchor = SlotHitTester.CaptureAnchor(Continuous, 215);
        Assert.Equal(1, anchor.SlotIndex);
        Assert.Equal(0, anchor.Fraction, 6);
    }

    [Fact]
    public void CaptureAnchor_BelowLastPage_AnchorsToLastPageWithFractionOne()
    {
        var anchor = SlotHitTester.CaptureAnchor(Continuous, 9999);
        Assert.Equal(1, anchor.SlotIndex);
        Assert.Equal(1, anchor.Fraction, 6);
    }

    [Fact]
    public void CaptureAnchor_NoSlots_ReturnsFirstSlotZero()
        => Assert.Equal(new SlotAnchor(0, 0), SlotHitTester.CaptureAnchor([], 100));

    [Fact]
    public void RestoreCenterY_AfterRelayout_KeepsSameFractionOfNewHeight()
    {
        PageSlot[] bigger = [new(0, 10, 10, 200, 400), new(1, 10, 420, 200, 400)];
        var anchor = new SlotAnchor(1, 0.5);
        Assert.Equal(620, SlotHitTester.RestoreCenterY(bigger, anchor)!.Value, 6);
    }

    [Fact]
    public void RestoreCenterY_SlotGone_ReturnsNull()
        => Assert.Null(SlotHitTester.RestoreCenterY(Continuous, new SlotAnchor(5, 0.5)));
}
