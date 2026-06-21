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

    private List<Rect> _pageRects = new();
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

        for (int i = 0; i < _pageRects.Count; i++)
        {
            var rect = _pageRects[i];
            if (rect.Contains(screenPoint))
            {
                double pdfX = (screenPoint.X - rect.Left) / scale;
                double pdfY = (screenPoint.Y - rect.Top) / scale;

                var hit = _objectManager.HitTest(i, new Point(pdfX, pdfY));
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
        int anchorPage = 0;
        double anchorFrac = 0;
        double centerY = PagesScrollViewer.VerticalOffset + PagesScrollViewer.ViewportHeight / 2;
        for (int i = 0; i < _pageRects.Count; i++)
        {
            var r = _pageRects[i];
            if (centerY < r.Bottom || i == _pageRects.Count - 1)
            {
                anchorPage = i;
                anchorFrac = r.Height > 0 ? Math.Clamp((centerY - r.Top) / r.Height, 0, 1) : 0;
                break;
            }
        }

        RefreshLayout(keepCache: false);

        // The ScrollViewer extent updates on the next layout pass, so restore after it.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (anchorPage < 0 || anchorPage >= _pageRects.Count) return;
            var nr = _pageRects[anchorPage];
            double newCenterY = nr.Top + anchorFrac * nr.Height;
            PagesScrollViewer.ScrollToVerticalOffset(newCenterY - PagesScrollViewer.ViewportHeight / 2);
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void RefreshLayout(bool keepCache = false)
    {
        if (_currentDocument == null) return;

        _pageRects.Clear();
        
        if (!keepCache)
        {
            _pageCache.Values.ToList().ForEach(b => b.Dispose());
            _pageCache.Clear();
        }

        double currentY = 0;
        double maxPageWidth = 0;
        float scale = (float)ZoomLevel;

        // Pass 1: Find the maximum page width
        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var pageSize = _currentDocument.Pages[i].Size;
            double w = pageSize.Width * scale;
            maxPageWidth = Math.Max(maxPageWidth, w);
        }

        // Determine container width. Fallback to ActualWidth if ViewportWidth is 0.
        double viewportWidth = PagesScrollViewer.ViewportWidth;
        if (viewportWidth == 0) viewportWidth = this.ActualWidth;
        
        double containerWidth = Math.Max(maxPageWidth, viewportWidth);

        // Pass 2: Calculate centered rects
        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var pageSize = _currentDocument.Pages[i].Size;
            double w = pageSize.Width * scale;
            double h = pageSize.Height * scale;
            
            // Center the page horizontally
            double x = (containerWidth - w) / 2;
            
            _pageRects.Add(new Rect(x, currentY, w, h));
            currentY += h + 20; // 20px spacing
        }

        InteractionCanvas.Width = containerWidth;
        InteractionCanvas.Height = currentY;
        
        skiaCanvas.InvalidateVisual();
    }

    private void ScrollToPage(int page)
    {
        if (page < 1 || page > _pageRects.Count) return;
        var rect = _pageRects[page - 1];
        PagesScrollViewer.ScrollToVerticalOffset(rect.Top);
    }

    private void OnPaintCanvas(object sender, SKPaintSurfaceEventArgs e)
    {
        if (_currentDocument == null || e.Surface == null) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.DimGray);

        float scale = (float)ZoomLevel;
        
        double viewTop = PagesScrollViewer.VerticalOffset;
        double viewBottom = viewTop + PagesScrollViewer.ViewportHeight;
        
        // Retrieve horizontal offset
        double viewLeft = PagesScrollViewer.HorizontalOffset;

        // Translate canvas for BOTH X and Y scrolling
        canvas.Translate((float)-viewLeft, (float)-viewTop);

        for (int i = 0; i < _pageRects.Count; i++)
        {
            var rect = _pageRects[i];
            
            // Only vertical culling is strictly necessary here, but horizontal culling could be added for performance if pages are very wide.
            if (rect.Bottom >= viewTop && rect.Top <= viewBottom)
            {
                _objectManager.MapPage(_currentDocument.Pages[i], i);

                if (!_pageCache.ContainsKey(i))
                {
                    _pageCache[i] = _renderEngine.RenderPage(_currentDocument.Pages[i], scale);
                }

                var bitmap = _pageCache[i];

                // Draw at the pre-calculated centered coordinates
                canvas.DrawBitmap(bitmap, (float)rect.Left, (float)rect.Top);

                using var paint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
                canvas.DrawRect((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, paint);

                // Draw yellow highlight rects for matching TextBlocks on this page
                DrawHighlights(canvas, i, rect, scale);
            }
            else
            {
                if (_pageCache.Count > 20 && _pageCache.ContainsKey(i))
                {
                    _pageCache[i].Dispose();
                    _pageCache.Remove(i);
                }
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

        // The page is laid out on the canvas at pageSize * scale (see _pageRects: w/h = pageSize * scale),
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
        if (_currentDocument == null || _pageRects.Count == 0) return;

        skiaCanvas.InvalidateVisual();

        double middleY = PagesScrollViewer.VerticalOffset + PagesScrollViewer.ViewportHeight / 2;
        for (int i = 0; i < _pageRects.Count; i++)
        {
            if (middleY >= _pageRects[i].Top && middleY <= _pageRects[i].Bottom)
            {
                int newPage = i + 1;
                if (CurrentPage != newPage)
                {
                    CurrentPage = newPage;
                }
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