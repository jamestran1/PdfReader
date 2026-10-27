using System.Windows.Media;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Dựng <see cref="PaintFrame"/> từ <see cref="ViewerFrameState"/> và giữ cache vị trí khớp từ khoá theo trang.
/// Không sở hữu <see cref="PageBitmapCache"/> hay <see cref="RenderEngine"/>: control Dispose chúng theo thứ tự cố định (CS-2).
/// </summary>
internal sealed class ViewerFrameComposer
{
    private readonly PageBitmapCache _pageCache;
    private readonly RenderEngine _renderEngine;
    private readonly Func<bool> _isDarkTheme;

    // Chạy trích xuất iText cho mỗi lần vẽ sẽ quá đắt, nên giữ kết quả theo trang cho từ khoá hiện tại.
    // Xoá khi đổi từ khoá hoặc đổi tài liệu.
    private string? _matchCacheQuery;
    private readonly Dictionary<int, IReadOnlyList<MatchRect>> _matchCache = [];

    // Vùng đoạn nguồn chỉ nằm trên một trang nên giữ một kết quả cho trích dẫn đang mở.
    private SourcePassage? _sourceCacheKey;
    private IReadOnlyList<MatchRect>? _sourceRects;

    public ViewerFrameComposer(PageBitmapCache pageCache, RenderEngine renderEngine, Func<bool>? isDarkTheme = null)
    {
        _pageCache = pageCache;
        _renderEngine = renderEngine;
        _isDarkTheme = isDarkTheme ?? (() => MaterialDesignThemeService.Current == AppTheme.Dark);
    }

    /// <summary>Bỏ cache vị trí khớp từ khoá (đổi tài liệu).</summary>
    public void ResetMatches()
    {
        _matchCache.Clear();
        _matchCacheQuery = null;
        _sourceCacheKey = null;
        _sourceRects = null;
    }

    /// <summary>Dựng khung rồi vẽ lên surface của lần paint; bỏ qua khi chưa có surface.</summary>
    public void Paint(PdfDocumentSession session, ViewerFrameState state, SKPaintSurfaceEventArgs e)
    {
        if (e.Surface == null) return;

        PdfPagePainter.Paint(e.Surface.Canvas, Compose(session, state, e.Info));
    }

    public PaintFrame Compose(PdfDocumentSession session, ViewerFrameState state, SKImageInfo info)
    {
        float dpiX = state.CanvasSize.Width > 0 ? (float)(info.Width / state.CanvasSize.Width) : 1f;
        float dpiY = state.CanvasSize.Height > 0 ? (float)(info.Height / state.CanvasSize.Height) : 1f;
        float scale = (float)state.Zoom;

        var overlays = new OverlayData(
            state.SelectionPageIndex, state.SelectionRects, state.HighlightQuery, state.SavedHighlights, state.CurrentDocumentId,
            ResolveBrushColor(state.SourcePassageResource, OverlayData.DefaultSourcePassageColor));
        var matchSource = state.MatchSource;
        var sources = new PageSources(
            pageIndex => GetPageBitmap(session, pageIndex, scale * dpiX),
            session.ImageRectsByPage,
            matchSource == null ? null : (pageIndex, query) => GetMatchRects(matchSource, pageIndex, query),
            pageIndex => session.GetPageSize(pageIndex).Height,
            SourceRectsFor(state));

        return new PaintFrame(
            state.Slots, scale, (dpiX, dpiY), state.Scroll, state.ViewportHeight,
            ResolveGutterColor(state.GutterResource), _isDarkTheme(), overlays, sources);
    }

    internal static SKColor ResolveGutterColor(object? resource) => ResolveBrushColor(resource, SKColors.DimGray);

    private static SKColor ResolveBrushColor(object? resource, SKColor fallback)
    {
        if (resource is SolidColorBrush brush)
        {
            Color c = brush.Color;
            return new SKColor(c.R, c.G, c.B, c.A);
        }
        return fallback;
    }

    // Render ở độ phân giải thiết bị (zoom * DPI) để trang vẫn nét khi canvas scale theo DIP.
    private SKBitmap GetPageBitmap(PdfDocumentSession session, int pageIndex, float renderScale)
    {
        var page = session.Document.Pages[pageIndex];
        session.ObjectManager.MapPage(page, pageIndex);
        return _pageCache.GetOrRender(pageIndex, () => _renderEngine.RenderPage(page, renderScale));
    }

    private Func<int, IReadOnlyList<MatchRect>>? SourceRectsFor(ViewerFrameState state)
    {
        if (state.MatchSource is not { } source || state.SourcePassage is not { } passage) return null;
        if (!passage.BelongsTo(state.CurrentDocumentId)) return null;
        return pageIndex => pageIndex == passage.PageIndex ? GetSourceRects(source, passage) : [];
    }

    /// <summary>Vùng của đoạn nguồn trên trang của nó, cache theo trích dẫn; control dùng lại để cuộn tới đó mà không trích xuất iText lần nữa.</summary>
    internal IReadOnlyList<MatchRect> GetSourceRects(IPdfDocumentService source, SourcePassage passage)
    {
        if (_sourceRects is null || !ReferenceEquals(_sourceCacheKey, passage))
        {
            var found = passage.Locate(fragments => source.FindPassageRects(passage.PageIndex, fragments));
            _sourceRects = PassageRectMerger.MergeLineRects(found, PassageRectMerger.DefaultMaxGapPt(found));
            _sourceCacheKey = passage;
        }
        return _sourceRects;
    }

    private IReadOnlyList<MatchRect> GetMatchRects(IPdfDocumentService source, int pageIndex, string query)
    {
        if (!string.Equals(_matchCacheQuery, query, StringComparison.Ordinal))
        {
            _matchCache.Clear();
            _matchCacheQuery = query;
        }
        if (!_matchCache.TryGetValue(pageIndex, out var rects))
        {
            rects = source.FindMatchRects(pageIndex, query);
            _matchCache[pageIndex] = rects;
        }
        return rects;
    }
}
