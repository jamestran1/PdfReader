using System;
using System.Collections.Generic;

namespace PdfReaderApp.Core;

public enum PdfViewMode { Continuous, SinglePage, ContinuousFacing, Facing }

public sealed record PageSlot(int PageIndex, double X, double Y, double Width, double Height);

public sealed record LayoutResult(
    IReadOnlyList<PageSlot> Slots,
    double ContentWidth,
    double ContentHeight,
    IReadOnlyList<int> UnitStartPages);

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

        // Build units (lists of page indices).
        var units = new List<List<int>>();
        if (!facing)
        {
            for (int i = 0; i < pages.Count; i++)
                units.Add(new List<int> { i });
        }
        else
        {
            int i = 0;
            if (showCover) { units.Add(new List<int> { 0 }); i = 1; }
            for (; i < pages.Count; i += 2)
            {
                var u = new List<int> { i };
                if (i + 1 < pages.Count) u.Add(i + 1);
                units.Add(u);
            }
        }

        int firstUnit = 0, lastUnit = units.Count - 1;
        if (!continuous)
        {
            int cur = 0;
            for (int u = 0; u < units.Count; u++)
                if (units[u].Contains(Math.Clamp(currentPageIndex, 0, pages.Count - 1))) { cur = u; break; }
            firstUnit = lastUnit = cur;
        }

        // Measure laid-out units first to know the widest (for ContentWidth + centering).
        var measured = new List<(List<int> pages, double width, double height)>();
        double maxUnitWidth = 0;
        for (int u = firstUnit; u <= lastUnit; u++)
        {
            double w = 0, h = 0;
            foreach (var pi in units[u])
            {
                double pw = pages[pi].WidthPt * scale;
                double ph = pages[pi].HeightPt * scale;
                w += pw;
                h = Math.Max(h, ph);
            }
            if (units[u].Count == 2) w += pageGap;
            measured.Add((units[u], w, h));
            maxUnitWidth = Math.Max(maxUnitWidth, w);
        }

        double contentWidth = Math.Max(viewportWidth, maxUnitWidth);
        double y = 0;
        foreach (var (u, w, h) in measured)
        {
            double startX = (contentWidth - w) / 2; // center the unit within the content area
            unitStarts.Add(u[0]);
            double x = startX;
            foreach (var pi in u)
            {
                double pw = pages[pi].WidthPt * scale;
                double ph = pages[pi].HeightPt * scale;
                double py = y + (h - ph) / 2; // vertical-center pages of differing height in the row
                slots.Add(new PageSlot(pi, x, py, pw, ph));
                x += pw + pageGap;
            }
            y += h + unitGap;
        }
        double contentHeight = measured.Count > 0 ? y - unitGap : 0;
        return new LayoutResult(slots, contentWidth, contentHeight, unitStarts);
    }
}
