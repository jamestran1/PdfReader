using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SkiaSharp;
using SkiaSharp.Views.WPF;
using SkiaSharp.Views.Desktop;
using PdfiumViewer.Core;
using PdfReaderApp.Core;
using PdfReaderApp.Core.Commands;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Controls;

public partial class PdfViewerControl : UserControl, IDisposable
{
    private PdfDocument? _currentDocument;
    private RenderEngine _renderEngine = new();
    private PdfObjectManager _objectManager = new();
    private Stack<IUndoCommand> _undoStack = new();
    private Dictionary<int, SKBitmap> _pageCache = new();
    private bool _disposed;

    private readonly List<Core.PageSlot> _slots = new();
    private PdfObjectManager.GhostText? _activeEditTarget;

    public static readonly DependencyProperty DocumentSourceProperty =
        DependencyProperty.Register("DocumentSource", typeof(string), typeof(PdfViewerControl), 
            new PropertyMetadata(null, OnDocumentSourceChanged));

    public string DocumentSource
    {
        get => (string)GetValue(DocumentSourceProperty);
        set => SetValue(DocumentSourceProperty, value);
    }

    public static readonly DependencyProperty CurrentPageProperty =
        DependencyProperty.Register("CurrentPage", typeof(int), typeof(PdfViewerControl), 
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnCurrentPageChanged));

    public int CurrentPage
    {
        get => (int)GetValue(CurrentPageProperty);
        set => SetValue(CurrentPageProperty, value);
    }

    public static readonly DependencyProperty TotalPagesProperty =
        DependencyProperty.Register("TotalPages", typeof(int), typeof(PdfViewerControl), 
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault));

    public int TotalPages
    {
        get => (int)GetValue(TotalPagesProperty);
        set => SetValue(TotalPagesProperty, value);
    }

    public static readonly DependencyProperty ZoomLevelProperty =
        DependencyProperty.Register("ZoomLevel", typeof(double), typeof(PdfViewerControl), 
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnZoomLevelChanged));

    public double ZoomLevel
    {
        get => (double)GetValue(ZoomLevelProperty);
        set => SetValue(ZoomLevelProperty, value);
    }

    // HighlightQuery: text to highlight on the visible page (bound to SelectedSearchQuery)
    public static readonly DependencyProperty HighlightQueryProperty =
        DependencyProperty.Register(nameof(HighlightQuery), typeof(string), typeof(PdfViewerControl),
            new PropertyMetadata(string.Empty, OnHighlightChanged));

    public string HighlightQuery
    {
        get => (string)GetValue(HighlightQueryProperty);
        set => SetValue(HighlightQueryProperty, value);
    }

    private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => ((PdfViewerControl)d).skiaCanvas.InvalidateVisual();

    // MatchSource: text engine that locates exact keyword rectangles (bound to MainViewModel.PdfService).
    public static readonly DependencyProperty MatchSourceProperty =
        DependencyProperty.Register(nameof(MatchSource), typeof(Services.IPdfDocumentService), typeof(PdfViewerControl),
            new PropertyMetadata(null, OnHighlightChanged));

    public Services.IPdfDocumentService? MatchSource
    {
        get => (Services.IPdfDocumentService?)GetValue(MatchSourceProperty);
        set => SetValue(MatchSourceProperty, value);
    }

    // Per-page cache of match rects for the current query; running iText extraction on every paint
    // would be far too expensive. Cleared when the query or the document changes.
    private string? _matchCacheQuery;
    private readonly Dictionary<int, List<Models.MatchRect>> _matchCache = new();

    private IReadOnlyList<Models.MatchRect> GetMatchRects(int pageIndex, string query)
    {
        if (_matchCacheQuery != query)
        {
            _matchCache.Clear();
            _matchCacheQuery = query;
        }
        if (!_matchCache.TryGetValue(pageIndex, out var rects))
        {
            rects = MatchSource?.FindMatchRects(pageIndex, query) ?? new List<Models.MatchRect>();
            _matchCache[pageIndex] = rects;
        }
        return rects;
    }

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
        {
            c.RefreshLayout(keepCache: false);
            // Single-unit modes show one unit at Y=0; reset scroll so it is in view after switching.
            if (c.ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing)
                c.PagesScrollViewer.ScrollToVerticalOffset(0);
        }
    }

    public PdfViewerControl()
    {
        InitializeComponent();
        this.Unloaded += PdfViewerControl_Unloaded;
        InteractionCanvas.MouseDown += OnCanvasMouseDown;
        
        this.SizeChanged += PdfViewerControl_SizeChanged;
        this.PreviewMouseWheel += PdfViewerControl_PreviewMouseWheel;
        
        TextEditor.EditingFinished += TextEditor_EditingFinished;
        TextEditor.EditingCancelled += TextEditor_EditingCancelled;
    }

    private void PdfViewerControl_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (_currentDocument != null)
        {
            RefreshLayout(keepCache: true);
        }
    }

    private void PdfViewerControl_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;

            if (_currentDocument == null) return;

            // 1. Capture initial state
            Point mousePos = e.GetPosition(PagesScrollViewer);
            double oldHOffset = PagesScrollViewer.HorizontalOffset;
            double oldVOffset = PagesScrollViewer.VerticalOffset;
            double oldZoom = ZoomLevel;

            double absoluteX = oldHOffset + mousePos.X;
            double absoluteY = oldVOffset + mousePos.Y;

            double relX = absoluteX / oldZoom;
            double relY = absoluteY / oldZoom;

            // 2. Calculate new zoom level
            double zoomDelta = e.Delta > 0 ? 0.1 : -0.1;
            double newZoom = oldZoom + zoomDelta;

            // Constrain zoom
            newZoom = Math.Max(0.1, Math.Min(5.0, newZoom));
            
            // If hitting limits, don't jump layout
            if (Math.Abs(newZoom - oldZoom) < 0.01) return;

            // This updates DependencyProperty and triggers RefreshLayout via OnZoomLevelChanged
            ZoomLevel = newZoom;

            // Force layout update so ScrollViewer knows about new InteractionCanvas size
            this.UpdateLayout();

            // 3. Adjust scroll offsets to anchor cursor
            double newAbsoluteX = relX * newZoom;
            double newAbsoluteY = relY * newZoom;

            double newHOffset = newAbsoluteX - mousePos.X;
            double newVOffset = newAbsoluteY - mousePos.Y;

            PagesScrollViewer.ScrollToHorizontalOffset(newHOffset);
            PagesScrollViewer.ScrollToVerticalOffset(newVOffset);
        }
        else if (ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing && _currentDocument != null)
        {
            // The current unit fills the view. If it is taller than the viewport, let the wheel scroll
            // within it first; only advance to the next/prev unit when already at the boundary.
            bool atTop = PagesScrollViewer.VerticalOffset <= 0.5;
            bool atBottom = PagesScrollViewer.VerticalOffset >= PagesScrollViewer.ScrollableHeight - 0.5;

            if (e.Delta < 0 && atBottom && CurrentPage < TotalPages)
            {
                e.Handled = true;
                if (ViewMode == Core.PdfViewMode.Facing)
                    CurrentPage = Math.Clamp(Core.PageLayoutCalculator.AdjacentFacingUnitFirstPage(ShowCover, TotalPages, CurrentPage - 1, forward: true) + 1, 1, TotalPages);
                else
                    CurrentPage = Math.Min(TotalPages, CurrentPage + 1);
                PagesScrollViewer.ScrollToVerticalOffset(0);
            }
            else if (e.Delta > 0 && atTop && CurrentPage > 1)
            {
                e.Handled = true;
                if (ViewMode == Core.PdfViewMode.Facing)
                    CurrentPage = Math.Clamp(Core.PageLayoutCalculator.AdjacentFacingUnitFirstPage(ShowCover, TotalPages, CurrentPage - 1, forward: false) + 1, 1, TotalPages);
                else
                    CurrentPage = Math.Max(1, CurrentPage - 1);
            }
        }
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (TextEditor.Visibility == Visibility.Visible)
        {
            HideEditor();
        }

        if (e.ClickCount == 2)
        {
            HandleDoubleClick(e);
        }
    }

    private void HandleDoubleClick(MouseButtonEventArgs e)
    {
        if (_currentDocument == null) return;

        var screenPoint = e.GetPosition(InteractionCanvas);
        float scale = (float)ZoomLevel;

        foreach (var slot in _slots)
        {
            var rect = new System.Windows.Rect(slot.X, slot.Y, slot.Width, slot.Height);
            if (rect.Contains(screenPoint))
            {
                double pdfX = (screenPoint.X - rect.Left) / scale;
                double pdfY = (screenPoint.Y - rect.Top) / scale;

                var hit = _objectManager.HitTest(slot.PageIndex, new Point(pdfX, pdfY));
                if (hit != null)
                {
                    ShowEditor(hit, rect, scale);
                }
                break;
            }
        }
    }

    private void ShowEditor(PdfObjectManager.GhostText hit, Rect pageRect, float scale)
    {
        _activeEditTarget = hit;
        
        Canvas.SetLeft(TextEditor, pageRect.Left + hit.Bounds.Left * scale - 5);
        Canvas.SetTop(TextEditor, pageRect.Top + hit.Bounds.Top * scale - 3);
        
        TextEditor.Visibility = Visibility.Visible;
        TextEditor.StartEditing(hit.Text, new Rect(0, 0, hit.Bounds.Width * scale, hit.Bounds.Height * scale));
    }

    private void TextEditor_EditingFinished(object? sender, string newText)
    {
        if (_activeEditTarget != null && _activeEditTarget.Text != newText)
        {
            var command = new EditTextCommand(_activeEditTarget.PageIndex, _activeEditTarget.CharIndex, _activeEditTarget.Text, newText);
            command.Execute();
            _undoStack.Push(command);
            
            _activeEditTarget.Text = newText;
        }
        HideEditor();
    }

    private void TextEditor_EditingCancelled(object? sender, EventArgs e)
    {
        HideEditor();
    }

    private void HideEditor()
    {
        TextEditor.Visibility = Visibility.Collapsed;
        _activeEditTarget = null;
        skiaCanvas.Focus();
    }

    private static void OnDocumentSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && e.NewValue is string path && !string.IsNullOrEmpty(path))
        {
            control.LoadDocument(path);
        }
    }

    private static void OnCurrentPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && control._currentDocument != null)
        {
            // Chế độ single-unit chỉ layout một đơn vị nên đổi trang phải re-layout.
            if (control.ViewMode is Core.PdfViewMode.SinglePage or Core.PdfViewMode.Facing)
                control.RefreshLayout(keepCache: true);
            else
                control.ScrollToPage((int)e.NewValue);
        }
    }

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && control._currentDocument != null)
        {
            control.RefreshLayoutPreservingAnchor();
        }
    }

    private void LoadDocument(string path)
    {
        try
        {
            DisposeCurrentDocument();

            if (!File.Exists(path)) return;

            byte[] fileBytes = File.ReadAllBytes(path);
            var ms = new MemoryStream(fileBytes);
            
            _currentDocument = PdfDocument.Load(ms);
            _matchCache.Clear();
            _matchCacheQuery = null;
            TotalPages = _currentDocument.PageCount;
            CurrentPage = 1;

            _objectManager.Clear();
            _undoStack.Clear();
            FitToViewport();   // zoom trang đầu vừa khung trước khi layout
            RefreshLayout();
            System.Diagnostics.Debug.WriteLine($"Successfully loaded PDF via Skia: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi mở file PDF: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    // Zoom changes every page size, so the absolute scroll offset would point at a different page
    // after relayout (the view "jumps far"). Anchor on the document point at the viewport center:
    // capture page + fraction before relayout, then restore the same point afterwards.
    private void RefreshLayoutPreservingAnchor()
    {
        int anchorSlot = 0;
        double anchorFrac = 0;
        double centerY = PagesScrollViewer.VerticalOffset + PagesScrollViewer.ViewportHeight / 2;
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

        // The ScrollViewer extent updates on the next layout pass, so restore after it.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (anchorSlot < 0 || anchorSlot >= _slots.Count) return;
            var ns = _slots[anchorSlot];
            double newCenterY = ns.Y + anchorFrac * ns.Height;
            PagesScrollViewer.ScrollToVerticalOffset(newCenterY - PagesScrollViewer.ViewportHeight / 2);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

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

        // Canh giữa theo bề rộng thực của canvas Skia (không phải ScrollViewer.ViewportWidth).
        // skiaCanvas a Margin=10, donc on utilise ActualWidth - 20 comme fallback.
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

    // Đặt zoom ban đầu sao cho trang đầu vừa khung nhìn (fit whole page), chừa lề nhỏ.
    private void FitToViewport()
    {
        if (_currentDocument == null || _currentDocument.PageCount == 0) return;
        double vpW = skiaCanvas.ActualWidth > 0 ? skiaCanvas.ActualWidth : this.ActualWidth - 20;
        double vpH = skiaCanvas.ActualHeight > 0 ? skiaCanvas.ActualHeight : this.ActualHeight - 20;
        if (vpW <= 0 || vpH <= 0) return;
        var size = _currentDocument.Pages[0].Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        double fit = Math.Min((vpW - 24) / size.Width, (vpH - 24) / size.Height);
        ZoomLevel = Math.Clamp(fit, 0.4, 4.0);
    }

    // Đặt zoom ban đầu sao cho trang đầu vừa khung nhìn (fit whole page), chừa lề nhỏ.
    public void FitToViewport()
    {
        if (_currentDocument == null || _currentDocument.PageCount == 0) return;
        double vpW = skiaCanvas.ActualWidth > 0 ? skiaCanvas.ActualWidth : this.ActualWidth - 20;
        double vpH = skiaCanvas.ActualHeight > 0 ? skiaCanvas.ActualHeight : this.ActualHeight - 20;
        if (vpW <= 0 || vpH <= 0) return;
        var size = _currentDocument.Pages[0].Size;
        if (size.Width <= 0 || size.Height <= 0) return;
        double fit = Math.Min((vpW - 24) / size.Width, (vpH - 24) / size.Height);
        ZoomLevel = Math.Clamp(fit, 0.4, 4.0);
    }

    private void ScrollToPage(int page)
    {
        int pageIndex = page - 1;
        var slot = _slots.FirstOrDefault(s => s.PageIndex == pageIndex);
        if (slot != null)
            PagesScrollViewer.ScrollToVerticalOffset(slot.Y);
    }

    private void OnPaintCanvas(object sender, SKPaintSurfaceEventArgs e)
    {
        if (_currentDocument == null || e.Surface == null) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.DimGray);

        // The Skia surface is in DEVICE PIXELS (ActualWidth * DPI scale), but all layout/scroll
        // coordinates below are in DIPs. On a >100% display this mismatch pushed the page into the
        // top-left and shrank it. Scale the canvas so DIP coordinates map onto the full pixel surface.
        float dpiX = skiaCanvas.ActualWidth > 0 ? (float)(e.Info.Width / skiaCanvas.ActualWidth) : 1f;
        float dpiY = skiaCanvas.ActualHeight > 0 ? (float)(e.Info.Height / skiaCanvas.ActualHeight) : 1f;
        canvas.Scale(dpiX, dpiY);

        float scale = (float)ZoomLevel;

        double viewTop = PagesScrollViewer.VerticalOffset;
        double viewBottom = viewTop + PagesScrollViewer.ViewportHeight;

        // Retrieve horizontal offset
        double viewLeft = PagesScrollViewer.HorizontalOffset;

        // Translate canvas for BOTH X and Y scrolling
        canvas.Translate((float)-viewLeft, (float)-viewTop);

        foreach (var slot in _slots)
        {
            var rect = new System.Windows.Rect(slot.X, slot.Y, slot.Width, slot.Height);
            if (rect.Bottom >= viewTop && rect.Top <= viewBottom)
            {
                _objectManager.MapPage(_currentDocument.Pages[slot.PageIndex], slot.PageIndex);

                // Render at device-pixel resolution (zoom * DPI) so the page stays crisp when the
                // canvas is scaled by DPI; draw into the DIP dest rect so it maps 1:1 in pixels.
                if (!_pageCache.ContainsKey(slot.PageIndex))
                    _pageCache[slot.PageIndex] = _renderEngine.RenderPage(_currentDocument.Pages[slot.PageIndex], scale * dpiX);

                var bitmap = _pageCache[slot.PageIndex];
                canvas.DrawBitmap(bitmap, SKRect.Create((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height));

                using var paint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
                canvas.DrawRect((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, paint);

                DrawHighlights(canvas, slot.PageIndex, rect, scale);
            }
        }
    }

    private void DrawHighlights(SKCanvas canvas, int pageIndex, System.Windows.Rect pageRect, float scale)
    {
        if (_currentDocument == null) return;

        string query = HighlightQuery;
        if (string.IsNullOrWhiteSpace(query) || MatchSource == null) return;

        // iText computes the exact keyword rectangles (PDF user-space) from the real glyph layout.
        var rects = GetMatchRects(pageIndex, query);
        if (rects.Count == 0) return;

        // The page is laid out on the canvas at pageSize * scale (PageLayoutCalculator scales slots by scale),
        // i.e. 1 PDF point = `scale` pixels. The highlight mapper must use the SAME pixels-per-point,
        // which means dpi=72 (ppp = scale * 72/72 = scale). Using 96 over-scales by 1.333x and the
        // rects drift away from the text (worse further from the origin).
        var pageSize = _currentDocument.Pages[pageIndex].Size;
        float pageHeightPt = (float)pageSize.Height;
        var mapper = new PdfCoordinateMapper(pageHeightPt, scale, 72);

        using var highlightPaint = new SKPaint
        {
            Color = new SKColor(255, 235, 59, 110),
            IsStroke = false
        };

        foreach (var m in rects)
        {
            // MatchRect (PdfX, PdfY) is the bottom-left; top = PdfY + Height (PDF Y grows upward).
            var (rx, ry) = mapper.PdfPointToRender(m.PdfX, m.PdfY + m.Height);
            var highlightRect = SKRect.Create(
                (float)pageRect.Left + rx,
                (float)pageRect.Top + ry,
                m.Width * scale,
                m.Height * scale);

            canvas.DrawRect(highlightRect, highlightPaint);
        }
    }

    private void PagesScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
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
    }

    private void DisposeCurrentDocument()
    {
        if (_currentDocument != null)
        {
            _pageCache.Values.ToList().ForEach(b => b.Dispose());
            _pageCache.Clear();
            _objectManager.Clear();
            _undoStack.Clear();
            _currentDocument.Dispose();
            _currentDocument = null;
        }
    }

    private void PdfViewerControl_Unloaded(object sender, RoutedEventArgs e)
    {
        Dispose();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                InteractionCanvas.MouseDown -= OnCanvasMouseDown;
                this.SizeChanged -= PdfViewerControl_SizeChanged;
                this.PreviewMouseWheel -= PdfViewerControl_PreviewMouseWheel;
                
                TextEditor.EditingFinished -= TextEditor_EditingFinished;
                TextEditor.EditingCancelled -= TextEditor_EditingCancelled;
                
                DisposeCurrentDocument();
                _renderEngine.Dispose();
            }
            _disposed = true;
        }
    }
}