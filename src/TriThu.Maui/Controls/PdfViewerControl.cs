using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Windows.Input;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Graphics;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using IPdfRenderService = PdfReaderApp.Services.IPdfRenderService;

namespace TriThu.Maui.Controls;

/// <summary>
/// MAUI port of the WPF PdfViewerControl.
/// Renders PDF pages via SkiaSharp into a virtualized scroll area.
/// Code-only (no XAML) — builds its visual tree in the constructor.
/// </summary>
public class PdfViewerControl : ContentView, IDisposable
{
    // ───────────────────────── Events ─────────────────────────
    public event Action<string>? LoadFailed;

    // ───────────────────────── Private state ─────────────────────────
    private IPdfRenderService? _pdfRender;
    private readonly PdfObjectManager _objectManager = new();
    private readonly Dictionary<int, SKBitmap> _pageCache = new();
    private Dictionary<int, IReadOnlyList<PdfImageLocator.NormalizedRect>> _imageRectsByPage = new();
    private bool _disposed;

    // Dark mode: invert lightness while keeping hue — easier on the eyes than straight RGB invert.
    private static readonly SKColorFilter DarkPageColorFilter =
        SKColorFilter.CreateHighContrast(new SKHighContrastConfig
        {
            Grayscale = false,
            InvertStyle = SKHighContrastConfigInvertStyle.InvertLightness,
            Contrast = 0f
        });

    private readonly List<PageSlot> _slots = new();

    // Match-rect cache (keyword search highlights). Cleared when query or document changes.
    private string? _matchCacheQuery;
    private readonly Dictionary<int, List<MatchRect>> _matchCache = new();

    // Text selection state
    private int _selPageIndex = -1;
    private int _anchorChar = -1;
    private bool _selecting;
    private string _selectionText = string.Empty;
    private readonly List<SKRect> _selectionRectsPdf = new(); // rects in PDF points for _selPageIndex

    // Pending scroll (after document load in continuous mode)
    private int _pendingScrollPage;
    private IDispatcherTimer? _scrollSettleTimer;
    private int _scrollSettleStable;
    private int _scrollSettleElapsed;

    // ───────────────────────── Visual tree elements ─────────────────────────
    private readonly ScrollView _scrollView;
    private readonly SKCanvasView _skiaCanvas;
    private readonly AbsoluteLayout _interactionOverlay;
    private readonly Button _addNoteButton;

    // ═══════════════════════════════════════════════════════════════════════
    //  BindableProperties (ported from DependencyProperties)
    // ═══════════════════════════════════════════════════════════════════════

    #region DocumentSource
    public static readonly BindableProperty DocumentSourceProperty =
        BindableProperty.Create(nameof(DocumentSource), typeof(string), typeof(PdfViewerControl),
            defaultValue: null, propertyChanged: OnDocumentSourceChanged);

    public string? DocumentSource
    {
        get => (string?)GetValue(DocumentSourceProperty);
        set => SetValue(DocumentSourceProperty, value);
    }

    private static void OnDocumentSourceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PdfViewerControl control && newValue is string path && !string.IsNullOrEmpty(path))
        {
            // Defer load until all bindings are wired (same rationale as WPF version).
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (control.DocumentSource == path)
                    control.LoadDocument(path);
            });
        }
    }
    #endregion

    #region CurrentPage
    public static readonly BindableProperty CurrentPageProperty =
        BindableProperty.Create(nameof(CurrentPage), typeof(int), typeof(PdfViewerControl),
            defaultValue: 1, defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnCurrentPageChanged);

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    private static void OnCurrentPageChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PdfViewerControl control && control._pdfRender != null)
        {
            if (control.ViewMode is PdfViewMode.SinglePage or PdfViewMode.Facing)
                control.RefreshLayout(keepCache: true);
            else
                control.ScrollToPage((int)newValue);
        }
    }
    #endregion

    #region TotalPages
    public static readonly BindableProperty TotalPagesProperty =
        BindableProperty.Create(nameof(TotalPages), typeof(int), typeof(PdfViewerControl),
            defaultValue: 1, defaultBindingMode: BindingMode.TwoWay);

    public int TotalPages
    {
        get => (int)GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }
    #endregion

    #region ZoomLevel
    public static readonly BindableProperty ZoomLevelProperty =
        BindableProperty.Create(nameof(ZoomLevel), typeof(double), typeof(PdfViewerControl),
            defaultValue: 1.0, defaultBindingMode: BindingMode.TwoWay,
            propertyChanged: OnZoomLevelChanged);

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    private static void OnZoomLevelChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PdfViewerControl control && control._pdfRender != null)
            control.RefreshLayoutPreservingAnchor();
    }
    #endregion

    #region HighlightQuery
    public static readonly BindableProperty HighlightQueryProperty =
        BindableProperty.Create(nameof(HighlightQuery), typeof(string), typeof(PdfViewerControl),
            defaultValue: string.Empty, propertyChanged: OnHighlightChanged);

    public string HighlightQuery
    {
        get => (string)GetValue(HighlightQueryProperty);
        set => SetValue(HighlightQueryProperty, value);
    }

    private static void OnHighlightChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PdfViewerControl)bindable)._skiaCanvas.InvalidateSurface();
    #endregion

    #region MatchSource
    public static readonly BindableProperty MatchSourceProperty =
        BindableProperty.Create(nameof(MatchSource), typeof(IPdfDocumentService), typeof(PdfViewerControl),
            defaultValue: null, propertyChanged: OnHighlightChanged);

    public IPdfDocumentService? MatchSource
    {
        get => (IPdfDocumentService?)GetValue(MatchSourceProperty);
        set => SetValue(MatchSourceProperty, value);
    }
    #endregion

    #region ViewMode
    public static readonly BindableProperty ViewModeProperty =
        BindableProperty.Create(nameof(ViewMode), typeof(PdfViewMode), typeof(PdfViewerControl),
            defaultValue: PdfViewMode.Continuous, propertyChanged: OnViewOptionChanged);

    public PdfViewMode ViewMode
    {
        get => (PdfViewMode)GetValue(ViewModeProperty);
        set => SetValue(ViewModeProperty, value);
    }
    #endregion

    #region ShowCover
    public static readonly BindableProperty ShowCoverProperty =
        BindableProperty.Create(nameof(ShowCover), typeof(bool), typeof(PdfViewerControl),
            defaultValue: true, propertyChanged: OnViewOptionChanged);

    public bool ShowCover
    {
        get => (bool)GetValue(ShowCoverProperty);
        set => SetValue(ShowCoverProperty, value);
    }
    #endregion

    #region AddNoteFromSelectionCommand
    public static readonly BindableProperty AddNoteFromSelectionCommandProperty =
        BindableProperty.Create(nameof(AddNoteFromSelectionCommand), typeof(ICommand), typeof(PdfViewerControl));

    public ICommand? AddNoteFromSelectionCommand
    {
        get => (ICommand?)GetValue(AddNoteFromSelectionCommandProperty);
        set => SetValue(AddNoteFromSelectionCommandProperty, value);
    }
    #endregion

    #region CurrentDocumentId
    public static readonly BindableProperty CurrentDocumentIdProperty =
        BindableProperty.Create(nameof(CurrentDocumentId), typeof(string), typeof(PdfViewerControl),
            defaultValue: null, propertyChanged: OnCurrentDocumentIdChanged);

    public string? CurrentDocumentId
    {
        get => (string?)GetValue(CurrentDocumentIdProperty);
        set => SetValue(CurrentDocumentIdProperty, value);
    }

    private static void OnCurrentDocumentIdChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PdfViewerControl)bindable).RepaintOverlay();
    #endregion

    #region Highlights
    public static readonly BindableProperty HighlightsProperty =
        BindableProperty.Create(nameof(Highlights), typeof(IEnumerable), typeof(PdfViewerControl),
            defaultValue: null, propertyChanged: OnHighlightsChanged);

    public IEnumerable? Highlights
    {
        get => (IEnumerable?)GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    private static void OnHighlightsChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var c = (PdfViewerControl)bindable;
        if (oldValue is INotifyCollectionChanged oldCol)
            oldCol.CollectionChanged -= c.OnHighlightsCollectionChanged;
        if (newValue is INotifyCollectionChanged newCol)
            newCol.CollectionChanged += c.OnHighlightsCollectionChanged;
        c.RepaintOverlay();
    }

    private void OnHighlightsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => RepaintOverlay();
    #endregion

    #region IsDarkMode
    public static readonly BindableProperty IsDarkModeProperty =
        BindableProperty.Create(nameof(IsDarkMode), typeof(bool), typeof(PdfViewerControl),
            defaultValue: false, propertyChanged: OnDarkModeChanged);

    public bool IsDarkMode
    {
        get => (bool)GetValue(IsDarkModeProperty);
        set => SetValue(IsDarkModeProperty, value);
    }

    private static void OnDarkModeChanged(BindableObject bindable, object oldValue, object newValue)
        => ((PdfViewerControl)bindable)._skiaCanvas.InvalidateSurface();
    #endregion

    // ═══════════════════════════════════════════════════════════════════════
    //  View option changed callback
    // ═══════════════════════════════════════════════════════════════════════

    private static void OnViewOptionChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is PdfViewerControl c && c._pdfRender != null)
        {
            c.RefreshLayout(keepCache: false);
            if (c.ViewMode is PdfViewMode.SinglePage or PdfViewMode.Facing)
                c._scrollView.ScrollToAsync(0, 0, animated: false);
            else
                c.ScheduleScrollToCurrentPage();
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Constructor — builds visual tree
    // ═══════════════════════════════════════════════════════════════════════

    public PdfViewerControl()
    {
        // SKCanvasView — the main rendering surface
        _skiaCanvas = new SKCanvasView();
        _skiaCanvas.PaintSurface += OnPaintCanvas;

        // "Add note" floating button (hidden by default)
        _addNoteButton = new Button
        {
            Text = "Thêm ghi chú",
            FontSize = 12,
            Padding = new Thickness(8, 2),
            IsVisible = false,
            ZIndex = 100
        };
        _addNoteButton.Clicked += AddNoteButton_Click;

        // Interaction overlay — positioned on top of the Skia canvas
        _interactionOverlay = new AbsoluteLayout
        {
            InputTransparent = false,
            BackgroundColor = Colors.Transparent,
            Children = { _addNoteButton }
        };

        // Attach pointer gesture recognizers for text selection
        var pointerGesture = new PointerGestureRecognizer();
        pointerGesture.PointerPressed += OnPointerPressed;
        pointerGesture.PointerMoved += OnPointerMoved;
        pointerGesture.PointerReleased += OnPointerReleased;
        _interactionOverlay.GestureRecognizers.Add(pointerGesture);

        // Pinch gesture for zoom
        var pinchGesture = new PinchGestureRecognizer();
        pinchGesture.PinchUpdated += OnPinchUpdated;
        _interactionOverlay.GestureRecognizers.Add(pinchGesture);

        // Grid to stack the Skia canvas and the interaction overlay
        var contentGrid = new Grid
        {
            Padding = new Thickness(10),
            BackgroundColor = Colors.DimGray
        };
        contentGrid.Children.Add(_skiaCanvas);
        contentGrid.Children.Add(_interactionOverlay);

        // ScrollView wrapping everything
        _scrollView = new ScrollView
        {
            Orientation = ScrollOrientation.Both,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Default,
            VerticalScrollBarVisibility = ScrollBarVisibility.Default,
            Content = contentGrid
        };
        _scrollView.Scrolled += OnScrollViewScrolled;

        Content = _scrollView;

        SizeChanged += OnSizeChanged;
        Unloaded += OnUnloaded;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Document loading
    // ═══════════════════════════════════════════════════════════════════════

    private void LoadDocument(string path)
    {
        try
        {
            DisposeCurrentDocument();

            if (!File.Exists(path)) return;

            byte[] fileBytes = File.ReadAllBytes(path);
            _pdfRender = new DocnetPdfRenderService();
            _pdfRender.LoadDocument(fileBytes);
            _matchCache.Clear();
            _matchCacheQuery = null;
            TotalPages = _pdfRender.PageCount;

            try { _imageRectsByPage = PdfImageLocator.GetNormalizedImageRectsByPage(path); }
            catch { _imageRectsByPage = new(); }

            // Honor target page set by VM before load (e.g. cross-doc jump).
            CurrentPage = Math.Clamp(CurrentPage, 1, _pdfRender.PageCount);

            ArmPendingScrollGuard();

            _objectManager.Clear();
            FitToViewport();
            RefreshLayout();

            ScheduleScrollToCurrentPage();
            System.Diagnostics.Debug.WriteLine($"Successfully loaded PDF via Skia: {path}");
        }
        catch (Exception ex)
        {
            LoadFailed?.Invoke($"Lỗi khi mở file PDF: {ex.Message}");
        }
    }

    private void DisposeCurrentDocument()
    {
        if (_pdfRender != null)
        {
            foreach (var bmp in _pageCache.Values) bmp.Dispose();
            _pageCache.Clear();
            _imageRectsByPage = new();
            _objectManager.Clear();
            _pdfRender.Dispose();
            _pdfRender = null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Layout
    // ═══════════════════════════════════════════════════════════════════════

    private void RefreshLayout(bool keepCache = false)
    {
        if (_pdfRender == null) return;

        _slots.Clear();
        if (!keepCache)
        {
            foreach (var bmp in _pageCache.Values) bmp.Dispose();
            _pageCache.Clear();
        }

        var sizes = new List<(double WidthPt, double HeightPt)>(_pdfRender.PageCount);
        for (int i = 0; i < _pdfRender.PageCount; i++)
        {
            var s = _pdfRender.GetPageSize(i);
            sizes.Add((s.Width, s.Height));
        }

        double viewportWidth = _skiaCanvas.Width > 0
            ? _skiaCanvas.Width
            : Math.Max(0, Width - 20);

        int currentPageIndex = Math.Clamp(CurrentPage - 1, 0, _pdfRender.PageCount - 1);
        var layout = PageLayoutCalculator.Compute(
            ViewMode, ShowCover, sizes,
            scale: ZoomLevel, viewportWidth: viewportWidth,
            pageGap: 12, unitGap: 20, currentPageIndex: currentPageIndex);

        _slots.AddRange(layout.Slots);

        // Size the overlay and canvas to match the content extent so ScrollView knows the scrollable area.
        _interactionOverlay.WidthRequest = layout.ContentWidth;
        _interactionOverlay.HeightRequest = layout.ContentHeight;
        _skiaCanvas.WidthRequest = layout.ContentWidth;
        _skiaCanvas.HeightRequest = layout.ContentHeight;

        _skiaCanvas.InvalidateSurface();
    }

    /// <summary>
    /// Preserve the document point at the viewport center across a zoom change.
    /// </summary>
    private void RefreshLayoutPreservingAnchor()
    {
        int anchorSlot = 0;
        double anchorFrac = 0;
        double centerY = _scrollView.ScrollY + _scrollView.Height / 2;
        for (int i = 0; i < _slots.Count; i++)
        {
            var s = _slots[i];
            if (centerY < s.Y + s.Height || i == _slots.Count - 1)
            {
                anchorSlot = i;
                anchorFrac = s.Height > 0 ? Math.Clamp((centerY - s.Y) / s.Height, 0, 1) : 0;
                break;
            }
        }

        RefreshLayout(keepCache: false);

        // Restore anchor after layout pass.
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            if (anchorSlot < 0 || anchorSlot >= _slots.Count) return;
            var ns = _slots[anchorSlot];
            double newCenterY = ns.Y + anchorFrac * ns.Height;
            double targetY = newCenterY - _scrollView.Height / 2;
            await _scrollView.ScrollToAsync(_scrollView.ScrollX, Math.Max(0, targetY), animated: false);
        });
    }

    /// <summary>
    /// Set initial zoom so the first page fits the viewport with small margins.
    /// </summary>
    public void FitToViewport()
    {
        if (_pdfRender == null || _pdfRender.PageCount == 0) return;
        double vpW = _skiaCanvas.Width > 0 ? _skiaCanvas.Width : Width - 20;
        double vpH = _skiaCanvas.Height > 0 ? _skiaCanvas.Height : Height - 20;
        if (vpW <= 0 || vpH <= 0) return;
        var (pageW, pageH) = _pdfRender.GetPageSize(0);
        if (pageW <= 0 || pageH <= 0) return;
        double fit = Math.Min((vpW - 24) / pageW, (vpH - 24) / pageH);
        ZoomLevel = Math.Clamp(fit, 0.4, 4.0);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Scrolling & page navigation
    // ═══════════════════════════════════════════════════════════════════════

    private void ScrollToPage(int page)
    {
        int pageIndex = page - 1;
        var slot = _slots.FirstOrDefault(s => s.PageIndex == pageIndex);
        if (slot != null)
            _scrollView.ScrollToAsync(_scrollView.ScrollX, slot.Y, animated: false);
    }

    private void ArmPendingScrollGuard()
    {
        _pendingScrollPage = (CurrentPage > 1 && ViewMode is PdfViewMode.Continuous or PdfViewMode.ContinuousFacing)
            ? CurrentPage : 0;
    }

    private void ScheduleScrollToCurrentPage()
    {
        if (!(CurrentPage > 1 && ViewMode is PdfViewMode.Continuous or PdfViewMode.ContinuousFacing))
        {
            _pendingScrollPage = 0;
            StopScrollSettleTimer();
            return;
        }
        _pendingScrollPage = CurrentPage;
        _scrollSettleStable = 0;
        _scrollSettleElapsed = 0;

        if (_scrollSettleTimer == null && Dispatcher != null)
        {
            _scrollSettleTimer = Dispatcher.CreateTimer();
            _scrollSettleTimer.Interval = TimeSpan.FromMilliseconds(30);
            _scrollSettleTimer.Tick += OnScrollSettleTick;
        }
        _scrollSettleTimer?.Start();
    }

    private void StopScrollSettleTimer() => _scrollSettleTimer?.Stop();

    private void OnScrollSettleTick(object? sender, EventArgs e)
    {
        if (_pendingScrollPage <= 0 || _pdfRender == null) { StopScrollSettleTimer(); return; }
        _scrollSettleElapsed++;

        var slot = _slots.FirstOrDefault(s => s.PageIndex == _pendingScrollPage - 1);
        if (slot == null)
        {
            if (_scrollSettleElapsed > 80) { _pendingScrollPage = 0; StopScrollSettleTimer(); }
            return;
        }

        double contentH = _scrollView.ContentSize.Height;
        double viewportH = _scrollView.Height;
        double scrollableHeight = Math.Max(0, contentH - viewportH);
        double target = Math.Min(slot.Y, scrollableHeight);
        double vOff = _scrollView.ScrollY;

        if (Math.Abs(vOff - target) > 1)
        {
            _scrollView.ScrollToAsync(_scrollView.ScrollX, target, animated: false);
            _scrollSettleStable = 0;
        }
        else
        {
            _scrollSettleStable++;
        }

        if (_scrollSettleStable >= 3 || _scrollSettleElapsed > 80)
        {
            _pendingScrollPage = 0;
            StopScrollSettleTimer();
        }
    }

    private void OnScrollViewScrolled(object? sender, ScrolledEventArgs e)
    {
        if (_pdfRender == null || _slots.Count == 0) return;

        // Guard: viewer hidden or viewport collapsed -> do not sync page.
        if (!IsVisible || _scrollView.Height <= 0) return;

        // Guard: pending scroll after document load -> do not overwrite CurrentPage.
        if (_pendingScrollPage > 0)
        {
            _skiaCanvas.InvalidateSurface();
            return;
        }

        // Clear selection when scrolling (avoids overlay drift).
        ClearSelection();

        _skiaCanvas.InvalidateSurface();

        double middleY = _scrollView.ScrollY + _scrollView.Height / 2;
        foreach (var slot in _slots)
        {
            if (middleY >= slot.Y && middleY <= slot.Y + slot.Height)
            {
                int newPage = slot.PageIndex + 1;
                if (CurrentPage != newPage) CurrentPage = newPage;
                break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  OnPaintCanvas — main render loop (SkiaSharp)
    // ═══════════════════════════════════════════════════════════════════════

    private void OnPaintCanvas(object? sender, SKPaintSurfaceEventArgs e)
    {
        if (_pdfRender == null || e.Surface == null) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(ResolveGutterColor());

        // DPI scaling: MAUI SKCanvasView surfaces are sized in device pixels.
        float dpiX = _skiaCanvas.Width > 0 ? (float)(e.Info.Width / _skiaCanvas.Width) : 1f;
        float dpiY = _skiaCanvas.Height > 0 ? (float)(e.Info.Height / _skiaCanvas.Height) : 1f;
        canvas.Scale(dpiX, dpiY);

        float scale = (float)ZoomLevel;

        double viewTop = _scrollView.ScrollY;
        double viewBottom = viewTop + _scrollView.Height;
        double viewLeft = _scrollView.ScrollX;

        // Translate for scroll offset
        canvas.Translate((float)-viewLeft, (float)-viewTop);

        foreach (var slot in _slots)
        {
            double slotRight = slot.X + slot.Width;
            double slotBottom = slot.Y + slot.Height;

            // Visibility culling
            if (slotBottom < viewTop || slot.Y > viewBottom) continue;

            _objectManager.MapPage(_pdfRender, slot.PageIndex);

            if (!_pageCache.ContainsKey(slot.PageIndex))
                _pageCache[slot.PageIndex] = _pdfRender.RenderPage(slot.PageIndex, scale * dpiX);

            var bitmap = _pageCache[slot.PageIndex];
            var dest = SKRect.Create((float)slot.X, (float)slot.Y, (float)slot.Width, (float)slot.Height);

            if (IsDarkMode)
            {
                using var invertPaint = new SKPaint { ColorFilter = DarkPageColorFilter };
                canvas.DrawBitmap(bitmap, dest, invertPaint);

                // Redraw image regions without inversion so photos look natural.
                if (_imageRectsByPage.TryGetValue(slot.PageIndex, out var imageRects))
                {
                    foreach (var img in imageRects)
                    {
                        var src = SKRect.Create(
                            (float)(img.Left * bitmap.Width), (float)(img.Top * bitmap.Height),
                            (float)(img.Width * bitmap.Width), (float)(img.Height * bitmap.Height));
                        var imgDst = SKRect.Create(
                            (float)(slot.X + img.Left * slot.Width), (float)(slot.Y + img.Top * slot.Height),
                            (float)(img.Width * slot.Width), (float)(img.Height * slot.Height));
                        canvas.DrawBitmap(bitmap, src, imgDst);
                    }
                }
            }
            else
            {
                canvas.DrawBitmap(bitmap, dest);
            }

            // Page border
            using var borderPaint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
            canvas.DrawRect(dest, borderPaint);

            // Overlays
            DrawHighlights(canvas, slot.PageIndex, slot, scale);
            DrawSelectionOverlay(canvas, slot.PageIndex, slot, scale);
            DrawSavedHighlights(canvas, slot.PageIndex, slot, scale);
        }
    }

    private SKColor ResolveGutterColor()
    {
        // In MAUI there is no TryFindResource equivalent; use a fixed dark/light gutter.
        return IsDarkMode ? new SKColor(30, 30, 30) : new SKColor(105, 105, 105); // DimGray
    }

    private void RepaintOverlay() => _skiaCanvas.InvalidateSurface();

    // ═══════════════════════════════════════════════════════════════════════
    //  Highlight / selection drawing helpers
    // ═══════════════════════════════════════════════════════════════════════

    private void DrawSavedHighlights(SKCanvas canvas, int pageIndex, PageSlot slot, float scale)
    {
        if (Highlights == null) return;
        foreach (var obj in Highlights)
        {
            if (obj is not Note note) continue;
            if (note.PageIndex != pageIndex || note.Rects == null) continue;
            if (CurrentDocumentId != null && note.DocumentId != null && note.DocumentId != CurrentDocumentId) continue;
            var color = ParseHighlightColor(note.Color);
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            foreach (var r in note.Rects)
            {
                canvas.DrawRect(SKRect.Create(
                    (float)(slot.X + r.X * scale),
                    (float)(slot.Y + r.Y * scale),
                    (float)(r.W * scale),
                    (float)(r.H * scale)), paint);
            }
        }
    }

    private static SKColor ParseHighlightColor(string? hex)
    {
        byte rr = 255, gg = 235, bb = 59;
        if (!string.IsNullOrEmpty(hex) && hex.StartsWith('#') && hex.Length == 7
            && byte.TryParse(hex.AsSpan(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var pr)
            && byte.TryParse(hex.AsSpan(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var pg)
            && byte.TryParse(hex.AsSpan(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var pb))
        { rr = pr; gg = pg; bb = pb; }
        return new SKColor(rr, gg, bb, 80);
    }

    private void DrawSelectionOverlay(SKCanvas canvas, int pageIndex, PageSlot slot, float scale)
    {
        if (_selPageIndex != pageIndex || _selectionRectsPdf.Count == 0 || _pdfRender == null) return;

        using var paint = new SKPaint { Color = new SKColor(33, 150, 243, 90), Style = SKPaintStyle.Fill };
        foreach (var r in _selectionRectsPdf)
        {
            canvas.DrawRect(SKRect.Create(
                (float)(slot.X + r.Left * scale),
                (float)(slot.Y + r.Top * scale),
                (float)(r.Width * scale),
                (float)(r.Height * scale)), paint);
        }
    }

    private void DrawHighlights(SKCanvas canvas, int pageIndex, PageSlot slot, float scale)
    {
        if (_pdfRender == null) return;

        string query = HighlightQuery;
        if (string.IsNullOrWhiteSpace(query) || MatchSource == null) return;

        var rects = GetMatchRects(pageIndex, query);
        if (rects.Count == 0) return;

        var (_, pgH) = _pdfRender.GetPageSize(pageIndex);
        float pageHeightPt = (float)pgH;
        var mapper = new PdfCoordinateMapper(pageHeightPt, scale, 72);

        using var highlightPaint = new SKPaint
        {
            Color = new SKColor(255, 235, 59, 110),
            IsStroke = false
        };

        foreach (var m in rects)
        {
            var (rx, ry) = mapper.PdfPointToRender(m.PdfX, m.PdfY + m.Height);
            var highlightRect = SKRect.Create(
                (float)slot.X + rx,
                (float)slot.Y + ry,
                m.Width * scale,
                m.Height * scale);

            canvas.DrawRect(highlightRect, highlightPaint);
        }
    }

    private IReadOnlyList<MatchRect> GetMatchRects(int pageIndex, string query)
    {
        if (_matchCacheQuery != query)
        {
            _matchCache.Clear();
            _matchCacheQuery = query;
        }
        if (!_matchCache.TryGetValue(pageIndex, out var rects))
        {
            rects = MatchSource?.FindMatchRects(pageIndex, query) ?? new List<MatchRect>();
            _matchCache[pageIndex] = rects;
        }
        return rects;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Text selection via pointer events
    // ═══════════════════════════════════════════════════════════════════════

    private void OnPointerPressed(object? sender, PointerEventArgs e)
    {
        if (_pdfRender == null) return;

        ClearSelection();

        var position = e.GetPosition(_interactionOverlay);
        if (position == null) return;

        if (TryPageHit(position.Value, out var slot, out var pdfPt))
        {
            _objectManager.MapPage(_pdfRender, slot.PageIndex);
            var chars = BuildSelChars(slot.PageIndex);
            int anchor = TextSelectionResolver.NearestCharIndex(chars, new SKPoint((float)pdfPt.X, (float)pdfPt.Y));
            if (anchor >= 0)
            {
                _selPageIndex = slot.PageIndex;
                _anchorChar = anchor;
                _selecting = true;
            }
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_selecting || _selPageIndex < 0) return;

        var position = e.GetPosition(_interactionOverlay);
        if (position == null) return;

        if (!TryPageHit(position.Value, out var slot, out var pdfPt) || slot.PageIndex != _selPageIndex)
            return;

        var chars = BuildSelChars(_selPageIndex);
        int focus = TextSelectionResolver.NearestCharIndex(chars, new SKPoint((float)pdfPt.X, (float)pdfPt.Y));
        if (focus < 0) return;

        var res = TextSelectionResolver.Resolve(chars, _anchorChar, focus);
        _selectionText = res.Text;
        _selectionRectsPdf.Clear();
        _selectionRectsPdf.AddRange(res.LineRects);

        _addNoteButton.IsVisible = false;
        _skiaCanvas.InvalidateSurface();
    }

    private void OnPointerReleased(object? sender, PointerEventArgs e)
    {
        if (!_selecting) return;
        _selecting = false;

        if (string.IsNullOrEmpty(_selectionText) || _selectionRectsPdf.Count == 0) return;

        // Position the "Add note" button near the end of the selection.
        var slot = _slots.FirstOrDefault(s => s.PageIndex == _selPageIndex);
        if (slot == null) return;

        float scale = (float)ZoomLevel;
        var last = _selectionRectsPdf[^1];
        double btnLeft = slot.X + last.Right * scale;
        double btnTop = slot.Y + last.Bottom * scale + 2;

        AbsoluteLayout.SetLayoutBounds(_addNoteButton, new Rect(btnLeft, btnTop, -1, -1));
        AbsoluteLayout.SetLayoutFlags(_addNoteButton, Microsoft.Maui.Layouts.AbsoluteLayoutFlags.None);
        _addNoteButton.IsVisible = true;
    }

    private void AddNoteButton_Click(object? sender, EventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectionText) && _selPageIndex >= 0)
        {
            var rects = _selectionRectsPdf
                .Select(r => new HighlightRect(r.Left, r.Top, r.Width, r.Height))
                .ToList();
            var sel = new NoteSelection(_selectionText, _selPageIndex, rects);
            if (AddNoteFromSelectionCommand?.CanExecute(sel) == true)
                AddNoteFromSelectionCommand.Execute(sel);
        }
        ClearSelection();
    }

    private void ClearSelection()
    {
        _selecting = false;
        _selPageIndex = -1;
        _anchorChar = -1;
        _selectionText = string.Empty;
        _selectionRectsPdf.Clear();
        _addNoteButton.IsVisible = false;
        _skiaCanvas.InvalidateSurface();
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Hit-testing helpers
    // ═══════════════════════════════════════════════════════════════════════

    private bool TryPageHit(Point screenPoint, out PageSlot slot, out Point pdfPoint)
    {
        float scale = (float)ZoomLevel;
        foreach (var s in _slots)
        {
            var rect = new Rect(s.X, s.Y, s.Width, s.Height);
            if (rect.Contains(screenPoint))
            {
                slot = s;
                pdfPoint = new Point((screenPoint.X - rect.Left) / scale, (screenPoint.Y - rect.Top) / scale);
                return true;
            }
        }
        slot = default!;
        pdfPoint = default;
        return false;
    }

    private List<SelChar> BuildSelChars(int pageIndex)
    {
        var list = new List<SelChar>();
        foreach (var g in _objectManager.GetPageTexts(pageIndex))
            list.Add(new SelChar(g.CharIndex, g.Text, g.Bounds));
        return list;
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Pinch-to-zoom
    // ═══════════════════════════════════════════════════════════════════════

    private double _pinchStartZoom;

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        switch (e.Status)
        {
            case GestureStatus.Started:
                _pinchStartZoom = ZoomLevel;
                break;

            case GestureStatus.Running:
                double newZoom = _pinchStartZoom * e.Scale;
                ZoomLevel = Math.Clamp(newZoom, 0.1, 5.0);
                break;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Size changed
    // ═══════════════════════════════════════════════════════════════════════

    private void OnSizeChanged(object? sender, EventArgs e)
    {
        if (_pdfRender != null)
            RefreshLayout(keepCache: true);
    }

    // ═══════════════════════════════════════════════════════════════════════
    //  Dispose
    // ═══════════════════════════════════════════════════════════════════════

    private void OnUnloaded(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (disposing)
        {
            _skiaCanvas.PaintSurface -= OnPaintCanvas;
            _scrollView.Scrolled -= OnScrollViewScrolled;
            _addNoteButton.Clicked -= AddNoteButton_Click;
            SizeChanged -= OnSizeChanged;
            Unloaded -= OnUnloaded;
            StopScrollSettleTimer();

            if (Highlights is INotifyCollectionChanged ncc)
                ncc.CollectionChanged -= OnHighlightsCollectionChanged;

            DisposeCurrentDocument();
        }
        _disposed = true;
    }
}
