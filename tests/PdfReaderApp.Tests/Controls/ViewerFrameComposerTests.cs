using System.IO;
using System.Windows.Media;
using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace PdfReaderApp.Tests.Controls;

public sealed class ViewerFrameComposerTests : IDisposable
{
    private readonly PageBitmapCache _cache = new();
    private readonly RenderEngine _engine = new();
    private readonly PdfDocumentSession _session = PdfDocumentSession.Open(SamplePath());

    public void Dispose()
    {
        _cache.Dispose();
        _session.Dispose();
        _engine.Dispose();
    }

    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return Path.Combine(directory.FullName, "scripts", "assets", "snapshot-sample.pdf");
    }

    private sealed class CountingMatchSource : IPdfDocumentService
    {
        public int Calls { get; private set; }

        public void LoadFile(string filePath) { }

        public IReadOnlyList<TextBlock> ExtractStructure() => [];

        public IReadOnlyList<PageText> ExtractPageTexts() => [];

        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query)
        {
            Calls++;
            return [new MatchRect(1, 2, 3, 4)];
        }

        public int PassageCalls { get; private set; }

        public IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments)
        {
            PassageCalls++;
            return [new MatchRect(5, 6, 7, 8)];
        }

        public void Dispose() { }
    }

    private ViewerFrameComposer Composer(bool dark = false) => new(_cache, _engine, () => dark);

    private static ViewerFrameState State(
        object? gutter = null, IPdfDocumentService? matches = null, string query = "abc",
        double zoom = 2, (double, double)? canvas = null, SourceHighlight? source = null)
        => new(
            [new PageSlot(0, 10, 20, 100, 200)], zoom, canvas ?? (400, 300), (5, 6), ViewportHeight: 300, gutter,
            SelectionPageIndex: 3, SelectionRects: [], HighlightQuery: query, SavedHighlights: null,
            CurrentDocumentId: "doc1", MatchSource: matches, SourceHighlight: source);

    private static readonly SKImageInfo Info = new(800, 600);

    [Fact]
    public void Compose_CopiesStateIntoFrame()
    {
        var state = State();

        var frame = Composer().Compose(_session, state, Info);

        Assert.Same(state.Slots, frame.Slots);
        Assert.Equal(2f, frame.Zoom);
        Assert.Equal((2f, 2f), frame.Dpi);
        Assert.Equal((5d, 6d), frame.Scroll);
        Assert.Equal(300, frame.ViewportHeight);
        Assert.Equal(3, frame.Overlays.SelectionPageIndex);
        Assert.Equal("abc", frame.Overlays.HighlightQuery);
        Assert.Equal("doc1", frame.Overlays.CurrentDocumentId);
        Assert.Same(_session.ImageRectsByPage, frame.Sources.ImageRects);
        Assert.Equal(842.0, frame.Sources.GetPageHeightPt(0), 0);
    }

    [Fact]
    public void Compose_ZeroCanvasSize_FallsBackToUnitDpi()
    {
        var frame = Composer().Compose(_session, State(canvas: (0, 0)), Info);

        Assert.Equal((1f, 1f), frame.Dpi);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Compose_DarkModeComesFromThemeProbe(bool dark)
    {
        var frame = Composer(dark).Compose(_session, State(), Info);

        Assert.Equal(dark, frame.DarkMode);
    }

    [Fact]
    public void Compose_SolidBrushGutter_UsesBrushColor()
    {
        var brush = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));

        var frame = Composer().Compose(_session, State(gutter: brush), Info);

        Assert.Equal(new SKColor(10, 20, 30, 255), frame.GutterColor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("không phải brush")]
    public void Compose_MissingOrNonBrushGutter_UsesDimGray(object? resource)
    {
        var frame = Composer().Compose(_session, State(gutter: resource), Info);

        Assert.Equal(SKColors.DimGray, frame.GutterColor);
    }

    [Fact]
    public void Compose_NoMatchSource_LeavesMatchRectsNull()
    {
        var frame = Composer().Compose(_session, State(), Info);

        Assert.Null(frame.Sources.GetMatchRects);
    }

    [Fact]
    public void MatchRects_SameQueryAndPage_HitsSourceOnce()
    {
        var source = new CountingMatchSource();
        var frame = Composer().Compose(_session, State(matches: source), Info);

        var first = frame.Sources.GetMatchRects!(0, "abc");
        var second = frame.Sources.GetMatchRects!(0, "abc");

        Assert.Same(first, second);
        Assert.Equal(1, source.Calls);
    }

    [Fact]
    public void MatchRects_NewQuery_ClearsCache()
    {
        var source = new CountingMatchSource();
        var frame = Composer().Compose(_session, State(matches: source), Info);

        frame.Sources.GetMatchRects!(0, "abc");
        frame.Sources.GetMatchRects!(0, "xyz");
        frame.Sources.GetMatchRects!(0, "abc");

        Assert.Equal(3, source.Calls);
    }

    [Fact]
    public void ResetMatches_ForcesSourceLookupAgain()
    {
        var source = new CountingMatchSource();
        var composer = Composer();
        var frame = composer.Compose(_session, State(matches: source), Info);
        frame.Sources.GetMatchRects!(0, "abc");

        composer.ResetMatches();
        frame.Sources.GetMatchRects!(0, "abc");

        Assert.Equal(2, source.Calls);
    }

    [Fact]
    public void Compose_SourceHighlightOfAnotherDocument_LeavesSourceRectsNull()
    {
        var source = new CountingMatchSource();
        var frame = Composer().Compose(
            _session, State(matches: source, source: new SourceHighlight("doc2", 0, ["abc"])), Info);

        Assert.Null(frame.Sources.GetSourceRects);
    }

    [Fact]
    public void SourceRects_OtherPage_IsEmptyWithoutLookup()
    {
        var source = new CountingMatchSource();
        var frame = Composer().Compose(
            _session, State(matches: source, source: new SourceHighlight("doc1", 2, ["abc"])), Info);

        Assert.Empty(frame.Sources.GetSourceRects!(0));
        Assert.Equal(0, source.PassageCalls);
    }

    [Fact]
    public void SourceRects_SameHighlightAcrossFrames_HitsSourceOnce()
    {
        var source = new CountingMatchSource();
        var highlight = new SourceHighlight("doc1", 0, ["abc"]);
        var composer = Composer();

        var first = composer.Compose(_session, State(matches: source, source: highlight), Info).Sources.GetSourceRects!(0);
        var second = composer.Compose(_session, State(matches: source, source: highlight), Info).Sources.GetSourceRects!(0);

        Assert.Equal([new MatchRect(5, 6, 7, 8)], first);
        Assert.Same(first, second);
        Assert.Equal(1, source.PassageCalls);
    }

    [Fact]
    public void SourceRects_NewHighlight_LooksUpAgain()
    {
        var source = new CountingMatchSource();
        var composer = Composer();

        composer.Compose(_session, State(matches: source, source: new SourceHighlight("doc1", 0, ["abc"])), Info).Sources.GetSourceRects!(0);
        composer.Compose(_session, State(matches: source, source: new SourceHighlight("doc1", 0, ["xyz"])), Info).Sources.GetSourceRects!(0);

        Assert.Equal(2, source.PassageCalls);
    }

    [Fact]
    public void PageBitmap_SamePage_ComesFromCache()
    {
        var frame = Composer().Compose(_session, State(), Info);

        var first = frame.Sources.GetPageBitmap(0);
        var second = frame.Sources.GetPageBitmap(0);

        Assert.Same(first, second);
        Assert.True(first.Width > 0);
    }

    [Fact]
    public void Paint_FillsSurfaceWithGutterColor()
    {
        var brush = new SolidColorBrush(Color.FromArgb(255, 10, 20, 30));
        using var surface = SKSurface.Create(Info);

        // Trang đặt ở (10,20) nên góc (790,590) chỉ có màu nền.
        Composer().Paint(_session, State(gutter: brush), new SKPaintSurfaceEventArgs(surface, Info));

        using var snapshot = surface.Snapshot();
        using var bitmap = SKBitmap.FromImage(snapshot);
        Assert.Equal(new SKColor(10, 20, 30, 255), bitmap.GetPixel(790, 590));
    }

    [Fact]
    public void Paint_NullSurface_DoesNothing()
    {
        Composer().Paint(_session, State(), new SKPaintSurfaceEventArgs(null!, Info));
    }
}
