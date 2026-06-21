# View Modes, Centering & Zoom Slider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add 4 view modes (Single Page, Continuous, Facing, Continuous Facing) with a cover-separation toggle, center each page/pair horizontally, and add a zoom slider.

**Architecture:** Extract page layout into a pure, unit-tested `PageLayoutCalculator` that maps (mode, showCover, page sizes, scale, viewport width) to centered page rectangles. `PdfViewerControl` calls it from `RefreshLayout`, stores the result as `List<PageSlot>`, and paints/scrolls from that. View state lives on `MainViewModel`; the toolbar binds toggle buttons and a zoom slider.

**Tech Stack:** WPF (.NET net10.0-windows), CommunityToolkit.Mvvm, SkiaSharp overlay rendering, PdfiumViewer, MaterialDesignThemes, xUnit.

## Global Constraints

- Target `net10.0-windows`; WPF + MVVM (CommunityToolkit.Mvvm).
- UI strings/tooltips tiếng Việt, GIỮ DẤU (UTF-8). KHÔNG dùng ký tự em dash.
- KHÔNG thêm `Co-Authored-By` trailer.
- Test: `dotnet test`; build: `dotnet build PdfReaderApp.slnx`.
- Center each unit within the canvas paint width (`skiaCanvas.ActualWidth`), not `ScrollViewer.ViewportWidth`.
- Zoom slider range 0.4–4.0; default view mode Continuous; ShowCover default true.

## File Structure

| File | Trách nhiệm | Hành động |
|---|---|---|
| `src/PdfReaderApp/Core/PageLayoutCalculator.cs` | Tính layout thuần (enum, slot, result) | Create |
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | State `ViewMode` + `ShowCoverSeparately` | Modify |
| `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs` | DP + RefreshLayout qua calculator + paint/scroll/nav | Modify |
| `src/PdfReaderApp/MainWindow.xaml(.cs)` | Toolbar: slider zoom + nút chế độ + toggle bìa + converter | Modify |

---

### Task 1: `PageLayoutCalculator` (pure)

**Files:**
- Create: `src/PdfReaderApp/Core/PageLayoutCalculator.cs`
- Test: `tests/PdfReaderApp.Tests/Core/PageLayoutCalculatorTests.cs`

**Interfaces:**
- Produces:
  - `enum PdfViewMode { Continuous, SinglePage, ContinuousFacing, Facing }`
  - `record PageSlot(int PageIndex, double X, double Y, double Width, double Height)`
  - `record LayoutResult(IReadOnlyList<PageSlot> Slots, double ContentWidth, double ContentHeight, IReadOnlyList<int> UnitStartPages)`
  - `static LayoutResult PageLayoutCalculator.Compute(PdfViewMode mode, bool showCover, IReadOnlyList<(double WidthPt, double HeightPt)> pages, double scale, double viewportWidth, double pageGap, double unitGap, int currentPageIndex)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Core/PageLayoutCalculatorTests.cs`:

```csharp
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
    public void EmptyDocument_ReturnsEmpty()
    {
        var r = PageLayoutCalculator.Compute(PdfViewMode.Continuous, true,
            new List<(double, double)>(), 1.0, 500, 10, 20, 0);
        Assert.Empty(r.Slots);
        Assert.Equal(0, r.ContentHeight, 3);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~PageLayoutCalculatorTests"`
Expected: FAIL compile (`PageLayoutCalculator` not found).

- [ ] **Step 3: Implement the calculator**

Create `src/PdfReaderApp/Core/PageLayoutCalculator.cs`:

```csharp
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
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~PageLayoutCalculatorTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Core/PageLayoutCalculator.cs tests/PdfReaderApp.Tests/Core/PageLayoutCalculatorTests.cs
git commit -m "feat: add pure PageLayoutCalculator for view modes and centering"
```

---

### Task 2: ViewModel state

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs` (property region near `_zoomLevel`)
- Test: `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `PdfViewMode` (Task 1).
- Produces: `ViewMode` (PdfViewMode, default `Continuous`), `ShowCoverSeparately` (bool, default `true`) observable properties.

- [ ] **Step 1: Write the failing tests**

Add to `tests/PdfReaderApp.Tests/MainViewModelTests.cs` (add `using PdfReaderApp.Core;` at top):

```csharp
[Fact]
public void ViewMode_DefaultsToContinuous()
{
    Assert.Equal(PdfViewMode.Continuous, new MainViewModel().ViewMode);
}

[Fact]
public void ShowCoverSeparately_DefaultsToTrue()
{
    Assert.True(new MainViewModel().ShowCoverSeparately);
}

[Fact]
public void ViewMode_CanChange()
{
    var vm = new MainViewModel { ViewMode = PdfViewMode.Facing };
    Assert.Equal(PdfViewMode.Facing, vm.ViewMode);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests.ViewMode|FullyQualifiedName~MainViewModelTests.ShowCover"`
Expected: FAIL compile (`ViewMode` not defined).

- [ ] **Step 3: Add the properties**

In `src/PdfReaderApp/ViewModels/MainViewModel.cs`, add `using PdfReaderApp.Core;` near the other usings, then add after the `_zoomLevel` property (around line 61):

```csharp
    [ObservableProperty]
    private PdfViewMode _viewMode = PdfViewMode.Continuous;

    [ObservableProperty]
    private bool _showCoverSeparately = true;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (all existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: add ViewMode and ShowCoverSeparately state to MainViewModel"
```

---

### Task 3: PdfViewerControl — layout via calculator, centering, slots

**Files:**
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`

**Interfaces:**
- Consumes: `PageLayoutCalculator.Compute`, `PageSlot`, `PdfViewMode`, `LayoutResult` (Task 1); `ViewMode`/`ShowCoverSeparately` bindings (Task 2).
- Produces: DPs `ViewMode` (PdfViewMode) and `ShowCover` (bool); `_slots` (`List<PageSlot>`) replacing `_pageRects`.

This task has no unit tests (layout math is covered by Task 1; this is WPF wiring). Verify by build + manual.

- [ ] **Step 1: Replace the `_pageRects` field with `_slots`**

Find the field declaration `private readonly List<Rect> _pageRects ... = new();` (near the top of the class) and replace it with:

```csharp
    private readonly List<Core.PageSlot> _slots = new();
```

- [ ] **Step 2: Add ViewMode and ShowCover dependency properties**

After the `MatchSource` DP block, add:

```csharp
    public static readonly DependencyProperty ViewModeProperty =
        DependencyProperty.Register(nameof(ViewMode), typeof(Core.PdfViewMode), typeof(PdfViewerControl),
            new PropertyMetadata(Core.PdfViewMode.Continuous, OnViewOptionChanged));

    public Core.PdfViewMode ViewMode
    {
        get => (Core.PdfViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }

    public static readonly DependencyProperty ShowCoverProperty =
        DependencyProperty.Register(nameof(ShowCover), typeof(bool), typeof(PdfViewerControl),
            new PropertyMetadata(true, OnViewOptionChanged));

    public bool ShowCover
    {
        get => (bool)GetValue(ShowCoverProperty);
        set => SetValue(ShowCoverProperty, value);
    }

    private static void OnViewOptionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl c && c._currentDocument != null)
            c.RefreshLayout(keepCache: false);
    }
```

- [ ] **Step 3: Rewrite `RefreshLayout` to use the calculator**

Replace the entire `RefreshLayout(bool keepCache = false)` method body (the Pass 1 / Pass 2 layout loop) with:

```csharp
    private void RefreshLayout(bool keepCache = false)
    {
        if (_currentDocument == null) return;

        _slots.Clear();
        if (!keepCache)
        {
            _pageCache.Values.ToList().ForEach(b => b.Dispose());
            _pageCache.Clear();
        }

        var sizes = new List<(double WidthPt, double HeightPt)>(_currentDocument.PageCount);
        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var s = _currentDocument.Pages[i].Size;
            sizes.Add((s.Width, s.Height));
        }

        // Center against the actual Skia paint width (not ScrollViewer.ViewportWidth, which is what
        // left-aligned the page before). skiaCanvas has Margin=10, so fall back to control width - 20.
        double viewportWidth = skiaCanvas.ActualWidth > 0
            ? skiaCanvas.ActualWidth
            : Math.Max(0, this.ActualWidth - 20);

        int currentPageIndex = Math.Clamp(CurrentPage - 1, 0, _currentDocument.PageCount - 1);
        var layout = Core.PageLayoutCalculator.Compute(
            ViewMode, ShowCover, sizes,
            scale: ZoomLevel, viewportWidth: viewportWidth,
            pageGap: 12, unitGap: 20, currentPageIndex: currentPageIndex);

        _slots.AddRange(layout.Slots);
        InteractionCanvas.Width = layout.ContentWidth;
        InteractionCanvas.Height = layout.ContentHeight;

        skiaCanvas.InvalidateVisual();
    }
```

- [ ] **Step 4: Update `OnPaintCanvas` to iterate slots**

Replace the `for (int i = 0; i < _pageRects.Count; i++) { ... }` loop in `OnPaintCanvas` with:

```csharp
        foreach (var slot in _slots)
        {
            var rect = new System.Windows.Rect(slot.X, slot.Y, slot.Width, slot.Height);
            if (rect.Bottom >= viewTop && rect.Top <= viewBottom)
            {
                _objectManager.MapPage(_currentDocument.Pages[slot.PageIndex], slot.PageIndex);

                if (!_pageCache.ContainsKey(slot.PageIndex))
                    _pageCache[slot.PageIndex] = _renderEngine.RenderPage(_currentDocument.Pages[slot.PageIndex], scale);

                var bitmap = _pageCache[slot.PageIndex];
                canvas.DrawBitmap(bitmap, (float)rect.Left, (float)rect.Top);

                using var paint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
                canvas.DrawRect((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, paint);

                DrawHighlights(canvas, slot.PageIndex, rect, scale);
            }
        }
```

(The previous `else { dispose cache if > 20 }` branch is dropped; cache is cleared on layout/zoom anyway. Pages off-screen simply are not drawn.)

- [ ] **Step 5: Update `ScrollToPage` and `PagesScrollViewer_ScrollChanged` to use slots**

Replace `ScrollToPage`:

```csharp
    private void ScrollToPage(int page)
    {
        int pageIndex = page - 1;
        var slot = _slots.FirstOrDefault(s => s.PageIndex == pageIndex);
        if (slot != null)
            PagesScrollViewer.ScrollToVerticalOffset(slot.Y);
    }
```

Replace the page-detection loop inside `PagesScrollViewer_ScrollChanged` (`if (_currentDocument == null || _pageRects.Count == 0) return;` ... the `for` loop) with:

```csharp
        if (_currentDocument == null || _slots.Count == 0) return;

        skiaCanvas.InvalidateVisual();

        double middleY = PagesScrollViewer.VerticalOffset + PagesScrollViewer.ViewportHeight / 2;
        foreach (var slot in _slots)
        {
            if (middleY >= slot.Y && middleY <= slot.Y + slot.Height)
            {
                int newPage = slot.PageIndex + 1;
                if (CurrentPage != newPage) CurrentPage = newPage;
                break;
            }
        }
```

- [ ] **Step 6: Make `OnCurrentPageChanged` relayout in single-unit modes**

Replace `OnCurrentPageChanged`:

```csharp
    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && control._currentDocument != null)
        {
            // Single-unit modes lay out only the current unit, so changing page must relayout.
            if (control.ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing)
                control.RefreshLayout(keepCache: true);
            else
                control.ScrollToPage((int)e.NewValue);
        }
    }
```

Also update `RefreshLayoutPreservingAnchor` if it references `_pageRects`: replace `_pageRects` with `_slots` and `_pageRects[i]` rect access with `new Rect(_slots[i].X, _slots[i].Y, _slots[i].Width, _slots[i].Height)`. (Anchor loop iterates the laid-out slots the same way.)

- [ ] **Step 7: Build and manual-verify**

Run: `dotnet build PdfReaderApp.slnx`
Expected: Build succeeded, 0 errors.

Manual (after Task 5 wires the toolbar; for now verify Continuous still works): open a PDF, confirm pages stack and are CENTERED (no left drift), highlight + zoom still work.

- [ ] **Step 8: Commit**

```bash
git add src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
git commit -m "feat: lay out pages via PageLayoutCalculator with correct centering and view-mode slots"
```

---

### Task 4: Single-unit wheel navigation

**Files:**
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs` (`PdfViewerControl_PreviewMouseWheel`)

**Interfaces:**
- Consumes: `ViewMode`, `_slots`, `CurrentPage`, `TotalPages`.

No unit test (input/scroll behavior); verify by build + manual.

- [ ] **Step 1: Add unit-advance handling for single-unit modes**

In `PdfViewerControl_PreviewMouseWheel`, the method currently handles `Ctrl+wheel` (zoom) in an `if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))` block. After that block (for the non-Ctrl case), add single-unit navigation. Insert this just before the method's closing brace (it runs only when Ctrl is NOT held, because the Ctrl branch returns/handles and the early code sets `e.Handled = true` then does zoom math — ensure this new code is in an `else`):

```csharp
        else if (ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing && _currentDocument != null)
        {
            // The current unit fills the view. If it is taller than the viewport, let the wheel scroll
            // within it first; only advance to the next/prev unit when already at the boundary.
            bool atTop = PagesScrollViewer.VerticalOffset <= 0.5;
            bool atBottom = PagesScrollViewer.VerticalOffset >= PagesScrollViewer.ScrollableHeight - 0.5;

            if (e.Delta < 0 && atBottom && CurrentPage < TotalPages)
            {
                e.Handled = true;
                int step = ViewMode == Core.PdfViewMode.Facing ? FacingStep(forward: true) : 1;
                CurrentPage = Math.Min(TotalPages, CurrentPage + step);
                PagesScrollViewer.ScrollToVerticalOffset(0);
            }
            else if (e.Delta > 0 && atTop && CurrentPage > 1)
            {
                e.Handled = true;
                int step = ViewMode == Core.PdfViewMode.Facing ? FacingStep(forward: false) : 1;
                CurrentPage = Math.Max(1, CurrentPage - step);
            }
        }
```

Add this helper method to the class:

```csharp
    // How many pages to jump to reach the next/prev facing unit from the current page.
    private int FacingStep(bool forward)
    {
        // With cover separation, the cover (page 1) is a unit of 1; other units are pairs.
        if (ShowCover)
            return CurrentPage == 1 ? 1 : 2; // from cover -> next pair start is +1; otherwise +2
        return 2;
    }
```

(If the existing wheel method is structured so the Ctrl case does not fall through, convert its top-level `if (Ctrl) { ... }` into `if (Ctrl) { ... }` followed by the `else if` above. Read the method first and adapt; do not duplicate the zoom logic.)

- [ ] **Step 2: Build and manual-verify**

Run: `dotnet build PdfReaderApp.slnx`
Expected: Build succeeded.

Manual (after Task 5): in Single Page / Facing, wheel down at bottom advances one unit, wheel up at top goes back; Next/Prev buttons also move one unit.

- [ ] **Step 3: Commit**

```bash
git add src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
git commit -m "feat: wheel-at-boundary unit navigation for single-page and facing modes"
```

---

### Task 5: Toolbar — zoom slider, mode buttons, cover toggle

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml` (zoom cluster in the top toolbar)
- Modify: `src/PdfReaderApp/MainWindow.xaml.cs` (add converter)

**Interfaces:**
- Consumes: `ZoomLevel`, `ViewMode`, `ShowCoverSeparately` (VM); `PdfViewerControl.ViewMode`/`ShowCover` DPs.
- Produces: `ViewModeToBoolConverter`.

No unit test; verify by build + manual.

- [ ] **Step 1: Add `ViewModeToBoolConverter`**

In `src/PdfReaderApp/MainWindow.xaml.cs`, after the existing converter classes (e.g. after `PageDisplayConverter`), add:

```csharp
// Checks a PdfViewMode against the mode name passed as ConverterParameter, for radio-style toggles.
public sealed class ViewModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object parameter, CultureInfo c)
        => value is PdfReaderApp.Core.PdfViewMode m && parameter is string p
           && string.Equals(m.ToString(), p, StringComparison.Ordinal);

    public object? ConvertBack(object value, Type t, object parameter, CultureInfo c)
        => value is true && parameter is string p
           && Enum.TryParse<PdfReaderApp.Core.PdfViewMode>(p, out var m) ? m : Binding.DoNothing;
}
```

(Ensure `using System.Windows.Data;` and `using System.Globalization;` and `using System;` are present in the file.)

- [ ] **Step 2: Register the converter**

In `src/PdfReaderApp/MainWindow.xaml`, in `<Window.Resources>` (after `PageDisplayConverter`), add:

```xml
        <local:ViewModeToBoolConverter x:Key="ViewModeToBoolConverter" />
```

- [ ] **Step 3: Add the zoom slider and view-mode buttons to the toolbar**

In `src/PdfReaderApp/MainWindow.xaml`, replace the zoom cluster (the `ZoomOut` button, the percentage `TextBlock`, and the `ZoomIn` button — currently between the two `MaterialDesignLightSeparator`s) with:

```xml
                        <Button Style="{StaticResource MaterialDesignIconButton}" Command="{Binding ZoomOutCommand}" ToolTip="Zoom Out">
                            <materialDesign:PackIcon Kind="Minus" />
                        </Button>
                        <Slider Width="120" VerticalAlignment="Center" Minimum="0.4" Maximum="4.0"
                                Value="{Binding ZoomLevel, Mode=TwoWay}"
                                TickFrequency="0.1" IsSnapToTickEnabled="False" ToolTip="Zoom"/>
                        <Button Style="{StaticResource MaterialDesignIconButton}" Command="{Binding ZoomInCommand}" ToolTip="Zoom In">
                            <materialDesign:PackIcon Kind="Plus" />
                        </Button>
                        <TextBlock Text="{Binding ZoomLevel, StringFormat={}{0:P0}}" VerticalAlignment="Center" Margin="8,0" Width="50" TextAlignment="Center" FontWeight="Bold"/>

                        <Separator Style="{StaticResource MaterialDesignLightSeparator}" Margin="16,0" />

                        <!-- View mode toggles (radio-style via ViewMode binding) -->
                        <ToggleButton Style="{StaticResource MaterialDesignFlatToggleButton}" ToolTip="Single Page"
                                      IsChecked="{Binding ViewMode, Converter={StaticResource ViewModeToBoolConverter}, ConverterParameter=SinglePage}">
                            <materialDesign:PackIcon Kind="Note" />
                        </ToggleButton>
                        <ToggleButton Style="{StaticResource MaterialDesignFlatToggleButton}" ToolTip="Continuous"
                                      IsChecked="{Binding ViewMode, Converter={StaticResource ViewModeToBoolConverter}, ConverterParameter=Continuous}">
                            <materialDesign:PackIcon Kind="ViewSequential" />
                        </ToggleButton>
                        <ToggleButton Style="{StaticResource MaterialDesignFlatToggleButton}" ToolTip="Facing"
                                      IsChecked="{Binding ViewMode, Converter={StaticResource ViewModeToBoolConverter}, ConverterParameter=Facing}">
                            <materialDesign:PackIcon Kind="BookOpenPageVariant" />
                        </ToggleButton>
                        <ToggleButton Style="{StaticResource MaterialDesignFlatToggleButton}" ToolTip="Continuous Facing"
                                      IsChecked="{Binding ViewMode, Converter={StaticResource ViewModeToBoolConverter}, ConverterParameter=ContinuousFacing}">
                            <materialDesign:PackIcon Kind="ViewGrid" />
                        </ToggleButton>
                        <ToggleButton Style="{StaticResource MaterialDesignFlatToggleButton}" ToolTip="Tách bìa (facing)"
                                      IsChecked="{Binding ShowCoverSeparately, Mode=TwoWay}">
                            <materialDesign:PackIcon Kind="BookOpenBlankVariant" />
                        </ToggleButton>
```

- [ ] **Step 4: Bind the new DPs on the PdfViewerControl**

In `src/PdfReaderApp/MainWindow.xaml`, on the `<controls:PdfViewerControl ... />` element, add these attributes:

```xml
                                       ViewMode="{Binding ViewMode}"
                                       ShowCover="{Binding ShowCoverSeparately}"
```

- [ ] **Step 5: Build and manual-verify**

Run: `dotnet build PdfReaderApp.slnx`
Expected: Build succeeded, no XAML/binding errors.

Manual: open a PDF; the toolbar shows a zoom slider + 4 mode buttons + cover toggle. Verify each mode (Single/Continuous/Facing/Continuous Facing), the cover toggle changes pairing, pages stay centered, the slider zooms, and the active mode button shows checked.

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml src/PdfReaderApp/MainWindow.xaml.cs
git commit -m "feat: toolbar zoom slider, view-mode toggles, cover toggle"
```

---

## Self-Review

**1. Spec coverage:**
- 4 view modes → Task 1 (calculator units) + Task 3 (rendering) + Task 5 (toggles). ✓
- Centering → Task 1 (center within ContentWidth) + Task 3 (viewport = skiaCanvas.ActualWidth). ✓
- Cover toggle → Task 1 (showCover) + Task 2 (state) + Task 5 (toggle). ✓
- Zoom slider 0.4–4.0 → Task 5. ✓
- Single-unit nav → Task 3 (OnCurrentPageChanged relayout) + Task 4 (wheel). ✓
- Testable layout unit → Task 1. ✓

**2. Placeholder scan:** Task 4 Step 1 instructs reading the existing wheel method before adapting (the Ctrl branch structure must be confirmed) — the code to add is fully specified; the adaptation note is necessary because the surrounding method is pre-existing and must not be duplicated. No TBD/TODO; all code blocks complete.

**3. Type consistency:** `PdfViewMode`, `PageSlot(PageIndex,X,Y,Width,Height)`, `LayoutResult(Slots,ContentWidth,ContentHeight,UnitStartPages)`, `Compute(mode,showCover,pages,scale,viewportWidth,pageGap,unitGap,currentPageIndex)` used identically across Tasks 1/3. `_slots` (List<PageSlot>) consistent across Task 3 consumers. DP names `ViewMode`/`ShowCover` (control) bind to VM `ViewMode`/`ShowCoverSeparately` (Task 5). Converter `ViewModeToBoolConverter` defined Task 5 Step 1, registered Step 2, used Step 3.
