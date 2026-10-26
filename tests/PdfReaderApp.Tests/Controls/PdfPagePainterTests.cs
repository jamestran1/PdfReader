using System.Windows;
using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using SkiaSharp;

namespace PdfReaderApp.Tests.Controls;

public class PdfPagePainterTests
{
    private const int SurfaceSize = 220;
    private const int PageSize = 100;
    private static readonly SKColor Gutter = new(30, 60, 90);
    private static readonly SKColor ImageColor = new(220, 20, 20);

    // Trang 100x100 ở (20,20), bitmap trắng có một khối ảnh đỏ ở nửa dưới phải (pixel 50..75).
    private static SKBitmap PageBitmap()
    {
        var bitmap = new SKBitmap(PageSize, PageSize);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.White);
        using var paint = new SKPaint { Color = ImageColor };
        canvas.DrawRect(50, 50, 25, 25, paint);
        return bitmap;
    }

    private static readonly IReadOnlyDictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> ImageRects =
        new Dictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>>
        {
            [0] = [new PdfImageLocator.NormalizedRect(0.5, 0.5, 0.25, 0.25)],
        };

    private static OverlayData NoOverlays() => new(-1, [], string.Empty, null, null);

    private static PaintFrame Frame(
        SKBitmap bitmap, bool dark = false, OverlayData? overlays = null, double scrollY = 0,
        Func<int, string, IReadOnlyList<MatchRect>>? matches = null)
    {
        var sources = new PageSources(_ => bitmap, ImageRects, matches, _ => PageSize);
        return new PaintFrame(
            [new PageSlot(0, 20, 20, PageSize, PageSize)],
            Zoom: 1f, Dpi: (1f, 1f), Scroll: (0, scrollY), ViewportHeight: SurfaceSize,
            Gutter, dark, overlays ?? NoOverlays(), sources);
    }

    private static SKBitmap Render(PaintFrame frame)
    {
        using var surface = SKSurface.Create(new SKImageInfo(SurfaceSize, SurfaceSize));
        PdfPagePainter.Paint(surface.Canvas, frame);
        using var image = surface.Snapshot();
        var result = new SKBitmap(SurfaceSize, SurfaceSize);
        image.ReadPixels(result.Info, result.GetPixels());
        return result;
    }

    [Fact]
    public void Paint_LightMode_DrawsGutterAndOriginalPage()
    {
        using var bitmap = PageBitmap();
        using var output = Render(Frame(bitmap));

        Assert.Equal(Gutter, output.GetPixel(5, 5));
        Assert.Equal(SKColors.White, output.GetPixel(40, 40));
        Assert.Equal(ImageColor, output.GetPixel(20 + 60, 20 + 60));
    }

    [Fact]
    public void Paint_DrawsBlackBorderAroundPage()
    {
        using var bitmap = PageBitmap();
        using var output = Render(Frame(bitmap));

        Assert.True(output.GetPixel(70, 20).Red < 140 || output.GetPixel(70, 19).Red < 140);
    }

    [Fact]
    public void Paint_DarkMode_InvertsPageLightnessButKeepsImageColor()
    {
        using var bitmap = PageBitmap();
        using var output = Render(Frame(bitmap, dark: true));

        var page = output.GetPixel(40, 40);
        Assert.True(page.Red < 60 && page.Green < 60 && page.Blue < 60, $"nền trang phải tối, thực tế {page}");
        Assert.Equal(ImageColor, output.GetPixel(20 + 60, 20 + 60));
        Assert.Equal(Gutter, output.GetPixel(5, 5));
    }

    [Fact]
    public void Paint_SkipsSlotsOutsideViewport()
    {
        using var bitmap = PageBitmap();
        int rendered = 0;
        var sources = new PageSources(_ => { rendered++; return bitmap; }, ImageRects, null, _ => PageSize);
        var frame = Frame(bitmap, scrollY: 500) with { Sources = sources };
        using var output = Render(frame);

        Assert.Equal(0, rendered);
        Assert.Equal(Gutter, output.GetPixel(40, 40));
    }

    [Fact]
    public void Paint_ScrollOffsetMovesPageUp()
    {
        using var bitmap = PageBitmap();
        using var output = Render(Frame(bitmap, scrollY: 10));

        Assert.Equal(SKColors.White, output.GetPixel(40, 15));
        Assert.Equal(Gutter, output.GetPixel(40, 5));
    }

    [Fact]
    public void Paint_SelectionOverlay_TintsOnlySelectedPage()
    {
        using var bitmap = PageBitmap();
        var overlays = NoOverlays() with { SelectionPageIndex = 0, SelectionRects = [new Rect(10, 10, 20, 20)] };
        using var output = Render(Frame(bitmap, overlays: overlays));

        var tinted = output.GetPixel(20 + 20, 20 + 20);
        Assert.InRange(tinted.Red, 170, 183);
        Assert.InRange(tinted.Blue, 248, 254);

        using var other = Render(Frame(bitmap, overlays: overlays with { SelectionPageIndex = 3 }));
        Assert.Equal(SKColors.White, other.GetPixel(40, 40));
    }

    [Fact]
    public void Paint_SearchMatch_FlipsBottomOriginRect()
    {
        using var bitmap = PageBitmap();
        var overlays = NoOverlays() with { HighlightQuery = "abc" };
        using var output = Render(Frame(bitmap, overlays: overlays, matches: (_, _) => [new MatchRect(10, 70, 20, 10)]));

        // Cạnh trên của hộp ở 100 - (70 + 10) = 20pt tính từ đỉnh trang, nên hộp phủ y 40..50 trên canvas.
        Assert.InRange(output.GetPixel(20 + 20, 20 + 25).Blue, 165, 175);
        Assert.Equal(SKColors.White, output.GetPixel(20 + 20, 20 + 60));
    }

    [Fact]
    public void Paint_OverlappingMatchRects_FillOnceSoOverlapIsNotDarker()
    {
        using var bitmap = PageBitmap();
        var overlays = NoOverlays() with { HighlightQuery = "abc" };
        // Hai hộp chồng nhau ở y 40..50 và 45..55 trên canvas (như dòng iText cao hơn khoảng cách dòng).
        using var output = Render(Frame(bitmap, overlays: overlays,
            matches: (_, _) => [new MatchRect(10, 70, 20, 10), new MatchRect(10, 65, 20, 10)]));

        var single = output.GetPixel(20 + 20, 20 + 22);
        var overlap = output.GetPixel(20 + 20, 20 + 27);
        Assert.NotEqual(SKColors.White, single);
        Assert.Equal(single, overlap);
    }

    [Fact]
    public void Paint_SearchMatch_NeedsQueryAndMatchSource()
    {
        using var bitmap = PageBitmap();
        static IReadOnlyList<MatchRect> Match(int page, string query) => [new MatchRect(10, 70, 20, 10)];

        using var noQuery = Render(Frame(bitmap, matches: Match));
        using var noSource = Render(Frame(bitmap, overlays: NoOverlays() with { HighlightQuery = "abc" }));

        Assert.Equal(SKColors.White, noQuery.GetPixel(40, 45));
        Assert.Equal(SKColors.White, noSource.GetPixel(40, 45));
    }

    [Fact]
    public void Paint_SavedHighlights_UseNoteColorAndFilterByDocument()
    {
        using var bitmap = PageBitmap();
        Note NoteOn(string documentId, int page) => new("n", "owner", documentId, page, null, "c", 0, 0,
            [new HighlightRect(10, 10, 20, 20)], "#FF0000");
        var overlays = NoOverlays() with
        {
            CurrentDocumentId = "doc-a",
            SavedHighlights = new object[] { NoteOn("doc-a", 0), NoteOn("doc-b", 0), NoteOn("doc-a", 1) },
        };
        using var output = Render(Frame(bitmap, overlays: overlays));

        // Alpha 80 của đỏ thuần phủ lên nền trắng: kênh xanh còn khoảng 175. Một lớp duy nhất, nên chỉ note đúng tài liệu và trang được vẽ.
        Assert.InRange(output.GetPixel(20 + 20, 20 + 20).Green, 171, 179);
    }

    [Fact]
    public void Paint_DpiScaleMapsDipOntoDevicePixels()
    {
        using var bitmap = PageBitmap();
        var frame = Frame(bitmap) with { Dpi = (2f, 2f) };
        using var output = Render(frame);

        Assert.Equal(Gutter, output.GetPixel(30, 30));
        Assert.Equal(SKColors.White, output.GetPixel(2 * 20 + 20, 2 * 20 + 20));
    }

    [Fact]
    public void PageBitmapCache_RendersOncePerPageAndDisposesOnClear()
    {
        using var cache = new PageBitmapCache();
        int renders = 0;
        SKBitmap Render() { renders++; return new SKBitmap(2, 2); }

        var first = cache.GetOrRender(3, Render);
        var again = cache.GetOrRender(3, Render);
        cache.Clear();
        var afterClear = cache.GetOrRender(3, Render);

        Assert.Same(first, again);
        Assert.Equal(2, renders);
        Assert.NotSame(first, afterClear);
        Assert.Equal(IntPtr.Zero, first.Handle);
    }
}
