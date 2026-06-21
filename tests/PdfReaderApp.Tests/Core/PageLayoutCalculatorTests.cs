using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class PageLayoutCalculatorTests
{
    // 4 pages, each 100x200 pt. scale=1. viewport=500. pageGap=10, unitGap=20.
    private static List<(double, double)> Pages(int n) =>
        Enumerable.Range(0, n).Select(_ => (100.0, 200.0)).ToList();

    private static LayoutResult Run(PdfViewMode mode, bool cover, int n, int current = 0, double vw = 500) =>
        PageLayoutCalculator.Compute(mode, cover, Pages(n), scale: 1.0, viewportWidth: vw,
            pageGap: 10, unitGap: 20, currentPageIndex: current);

    [Fact]
    public void Continuous_StacksAllPages_Centered()
    {
        var r = Run(PdfViewMode.Continuous, false, 3);
        Assert.Equal(3, r.Slots.Count);
        // each page 100 wide, centered in 500 -> X = 200
        Assert.All(r.Slots, s => Assert.Equal(200, s.X, 3));
        // stacked: Y = 0, 220, 440
        Assert.Equal(new[] { 0.0, 220.0, 440.0 }, r.Slots.Select(s => s.Y));
        Assert.Equal(500, r.ContentWidth, 3);
        Assert.Equal(640, r.ContentHeight, 3); // 3*200 + 2*20
        Assert.Equal(new[] { 0, 1, 2 }, r.UnitStartPages);
    }

    [Fact]
    public void SinglePage_LaysOutOnlyCurrentPage()
    {
        var r = Run(PdfViewMode.SinglePage, false, 5, current: 2);
        Assert.Single(r.Slots);
        Assert.Equal(2, r.Slots[0].PageIndex);
        Assert.Equal(0, r.Slots[0].Y, 3);
        Assert.Equal(new[] { 2 }, r.UnitStartPages);
    }

    [Fact]
    public void Facing_ShowCover_FirstUnitIsCoverAlone_ThenPairs()
    {
        // current=0 -> cover unit (page 0 alone)
        var cover = Run(PdfViewMode.Facing, true, 5, current: 0);
        Assert.Single(cover.Slots);
        Assert.Equal(0, cover.Slots[0].PageIndex);
        // cover (100 wide) centered in 500 -> X=200
        Assert.Equal(200, cover.Slots[0].X, 3);

        // current=1 -> pair (1,2)
        var pair = Run(PdfViewMode.Facing, true, 5, current: 1);
        Assert.Equal(2, pair.Slots.Count);
        Assert.Equal(new[] { 1, 2 }, pair.Slots.Select(s => s.PageIndex));
        // pair width = 100 + 10 + 100 = 210, centered in 500 -> startX = 145
        Assert.Equal(145, pair.Slots[0].X, 3);
        Assert.Equal(255, pair.Slots[1].X, 3); // 145 + 100 + 10
    }

    [Fact]
    public void Facing_NoCover_PairsFromStart()
    {
        var r = Run(PdfViewMode.Facing, false, 4, current: 0);
        Assert.Equal(new[] { 0, 1 }, r.Slots.Select(s => s.PageIndex));
        var r2 = Run(PdfViewMode.Facing, false, 4, current: 2);
        Assert.Equal(new[] { 2, 3 }, r2.Slots.Select(s => s.PageIndex));
    }

    [Fact]
    public void ContinuousFacing_ShowCover_StacksCoverThenPairs()
    {
        var r = Run(PdfViewMode.ContinuousFacing, true, 5);
        // units: (0), (1,2), (3,4) -> 5 slots, 3 unit rows
        Assert.Equal(5, r.Slots.Count);
        Assert.Equal(new[] { 0, 1, 3 }, r.UnitStartPages);
        // row Y: cover at 0; pair (1,2) at 220; pair (3,4) at 440
        Assert.Equal(0, r.Slots.First(s => s.PageIndex == 0).Y, 3);
        Assert.Equal(220, r.Slots.First(s => s.PageIndex == 1).Y, 3);
        Assert.Equal(440, r.Slots.First(s => s.PageIndex == 3).Y, 3);
    }

    [Fact]
    public void WideUnit_ExceedsViewport_PinnedLeft_ContentWidthGrows()
    {
        // pair width 210 with viewport=150 -> contentWidth=210, unit X = 0
        var r = Run(PdfViewMode.Facing, false, 2, current: 0, vw: 150);
        Assert.Equal(210, r.ContentWidth, 3);
        Assert.Equal(0, r.Slots[0].X, 3);
    }

    [Fact]
    public void Facing_NoCover_OddPageCount_LastUnitIsSingleton()
    {
        // 3 pages, no cover: units (0,1) then (2). current=2 -> singleton last unit.
        var r = Run(PdfViewMode.Facing, false, 3, current: 2);
        Assert.Single(r.Slots);
        Assert.Equal(2, r.Slots[0].PageIndex);
        // single page (100 wide) centered in 500 -> X=200
        Assert.Equal(200, r.Slots[0].X, 3);
    }

    [Fact]
    public void EmptyDocument_ReturnsEmpty()
    {
        var r = PageLayoutCalculator.Compute(PdfViewMode.Continuous, true,
            new List<(double, double)>(), 1.0, 500, 10, 20, 0);
        Assert.Empty(r.Slots);
        Assert.Equal(0, r.ContentHeight, 3);
    }

    [Fact]
    public void AdjacentFacingUnitFirstPage_ShowCover_NavigatesUnits()
    {
        // units: (0),(1,2),(3,4)
        Assert.Equal(1, PageLayoutCalculator.AdjacentFacingUnitFirstPage(true, 5, 0, forward: true));  // cover -> pair
        Assert.Equal(0, PageLayoutCalculator.AdjacentFacingUnitFirstPage(true, 5, 2, forward: false)); // pair(1,2) -> cover
        Assert.Equal(1, PageLayoutCalculator.AdjacentFacingUnitFirstPage(true, 5, 4, forward: false)); // pair(3,4) -> pair(1,2)
        Assert.Equal(3, PageLayoutCalculator.AdjacentFacingUnitFirstPage(true, 5, 2, forward: true));  // pair(1,2) -> pair(3,4)
    }

    [Fact]
    public void AdjacentFacingUnitFirstPage_NoCover_NavigatesUnits()
    {
        // units: (0,1),(2,3)
        Assert.Equal(2, PageLayoutCalculator.AdjacentFacingUnitFirstPage(false, 4, 0, forward: true));  // (0,1)->(2,3)
        Assert.Equal(0, PageLayoutCalculator.AdjacentFacingUnitFirstPage(false, 4, 3, forward: false)); // (2,3)->(0,1)
    }

    [Fact]
    public void AdjacentFacingUnitFirstPage_ClampsAtEnds()
    {
        Assert.Equal(0, PageLayoutCalculator.AdjacentFacingUnitFirstPage(true, 5, 0, forward: false)); // cover, back -> stays cover
        Assert.Equal(3, PageLayoutCalculator.AdjacentFacingUnitFirstPage(true, 5, 4, forward: true));  // last pair, fwd -> stays
    }
}
