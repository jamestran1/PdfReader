namespace PdfReaderApp.Core;

/// <summary>
/// Pure layout: maps (mode, cover, page sizes, scale, viewport) to centered page rectangles.
/// A "unit" is one row shown together: 1 page (single modes) or 2 pages (facing). Continuous
/// modes lay out every unit stacked vertically; single-unit modes lay out only the unit that
/// contains currentPageIndex. Each unit is centered horizontally within ContentWidth.
/// </summary>
public static class PageLayoutCalculator
{
    public static LayoutResult Compute(
        PdfViewMode mode, bool showCover,
        IReadOnlyList<(double WidthPt, double HeightPt)> pages,
        double scale, double viewportWidth,
        double pageGap, double unitGap, int currentPageIndex)
    {
        var slots = new List<PageSlot>();
        var unitStarts = new List<int>();
        if (pages.Count == 0)
            return new LayoutResult(slots, viewportWidth, 0, unitStarts);

        bool facing = mode is PdfViewMode.Facing or PdfViewMode.ContinuousFacing;
        bool continuous = mode is PdfViewMode.Continuous or PdfViewMode.ContinuousFacing;

        var metrics = new UnitMetrics(scale, pageGap);
        var units = BuildUnits(pages.Count, facing, showCover);
        // Clamp (don't throw): the UI may pass a transient out-of-range page during navigation.
        var (firstUnit, lastUnit) = continuous
            ? (0, units.Count - 1)
            : SingleUnitRange(units, Math.Clamp(currentPageIndex, 0, pages.Count - 1));

        // Measure laid-out units first to know the widest (for ContentWidth + centering).
        var measured = new List<(List<int> pages, double width, double height)>();
        double maxUnitWidth = 0;
        for (int u = firstUnit; u <= lastUnit; u++)
        {
            var (w, h) = MeasureUnit(units[u], pages, metrics);
            measured.Add((units[u], w, h));
            maxUnitWidth = Math.Max(maxUnitWidth, w);
        }

        double contentWidth = Math.Max(viewportWidth, maxUnitWidth);
        double y = 0;
        foreach (var (u, w, h) in measured)
        {
            unitStarts.Add(u[0]);
            PlaceUnit(slots, u, pages, metrics, new RowBox(StartX: (contentWidth - w) / 2, Y: y, Height: h));
            y += h + unitGap;
        }
        double contentHeight = measured.Count > 0 ? y - unitGap : 0;
        return new LayoutResult(slots, contentWidth, contentHeight, unitStarts);
    }

    private static List<List<int>> BuildUnits(int pageCount, bool facing, bool showCover)
    {
        var units = new List<List<int>>();
        if (!facing)
        {
            for (int i = 0; i < pageCount; i++)
                units.Add([i]);
            return units;
        }

        int next = 0;
        if (showCover) { units.Add([0]); next = 1; }
        for (; next < pageCount; next += 2)
        {
            var unit = new List<int> { next };
            if (next + 1 < pageCount) unit.Add(next + 1);
            units.Add(unit);
        }
        return units;
    }

    private static (int First, int Last) SingleUnitRange(List<List<int>> units, int clampedPageIndex)
    {
        int cur = 0;
        for (int u = 0; u < units.Count; u++)
            if (units[u].Contains(clampedPageIndex)) { cur = u; break; }
        return (cur, cur);
    }

    private static (double Width, double Height) MeasureUnit(
        List<int> unit, IReadOnlyList<(double WidthPt, double HeightPt)> pages, UnitMetrics metrics)
    {
        double w = 0, h = 0;
        foreach (var pi in unit)
        {
            w += pages[pi].WidthPt * metrics.Scale;
            h = Math.Max(h, pages[pi].HeightPt * metrics.Scale);
        }
        if (unit.Count == 2) w += metrics.PageGap;
        return (w, h);
    }

    private static void PlaceUnit(
        List<PageSlot> slots, List<int> unit, IReadOnlyList<(double WidthPt, double HeightPt)> pages,
        UnitMetrics metrics, RowBox row)
    {
        double x = row.StartX;
        for (int i = 0; i < unit.Count; i++)
        {
            int pi = unit[i];
            double pw = pages[pi].WidthPt * metrics.Scale;
            double ph = pages[pi].HeightPt * metrics.Scale;
            double py = row.Y + (row.Height - ph) / 2; // vertical-center pages of differing height in the row
            slots.Add(new PageSlot(pi, x, py, pw, ph));
            x += pw;
            if (i < unit.Count - 1) x += metrics.PageGap;
        }
    }

    private sealed record UnitMetrics(double Scale, double PageGap);

    private sealed record RowBox(double StartX, double Y, double Height);

    /// <summary>
    /// 0-based first page of the facing unit adjacent (forward/back) to the unit containing
    /// currentPageIndex. Mirrors the unit grouping in Compute so navigation stays consistent
    /// with the layout. Clamped to the first/last unit at the ends.
    /// </summary>
    public static int AdjacentFacingUnitFirstPage(bool showCover, int pageCount, int currentPageIndex, bool forward)
    {
        if (pageCount <= 0) return 0;
        var units = new List<List<int>>();
        int i = 0;
        if (showCover) { units.Add([0]); i = 1; }
        for (; i < pageCount; i += 2)
        {
            var u = new List<int> { i };
            if (i + 1 < pageCount) u.Add(i + 1);
            units.Add(u);
        }
        int clamped = Math.Clamp(currentPageIndex, 0, pageCount - 1);
        int cur = 0;
        for (int u = 0; u < units.Count; u++)
            if (units[u].Contains(clamped)) { cur = u; break; }
        int target = Math.Clamp(cur + (forward ? 1 : -1), 0, units.Count - 1);
        return units[target][0];
    }
}
