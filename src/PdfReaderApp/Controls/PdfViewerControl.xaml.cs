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

namespace PdfReaderApp.Controls;

public partial class PdfViewerControl : UserControl, IDisposable
{
    private PdfDocument? _currentDocument;
    private RenderEngine _renderEngine = new();
    private PdfObjectManager _objectManager = new();
    private Dictionary<int, SKBitmap> _pageCache = new();
    private bool _disposed;

    private List<Rect> _pageRects = new();

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

    public PdfViewerControl()
    {
        InitializeComponent();
        this.Unloaded += PdfViewerControl_Unloaded;
        skiaCanvas.MouseDown += OnCanvasMouseDown;
    }

    private void OnCanvasMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_currentDocument == null) return;

        var screenPoint = e.GetPosition(skiaCanvas);
        float scale = (float)ZoomLevel;

        // Find which page was clicked
        for (int i = 0; i < _pageRects.Count; i++)
        {
            var rect = _pageRects[i];
            if (rect.Contains(screenPoint))
            {
                // Convert screen point to PDF point (origin bottom-left usually, but Pdfium often uses top-left for text)
                // Let's assume top-left for now as Pdfium text API usually does.
                double pdfX = (screenPoint.X - rect.Left) / scale;
                double pdfY = (screenPoint.Y - rect.Top) / scale;

                var hit = _objectManager.HitTest(i, new Point(pdfX, pdfY));
                if (hit != null)
                {
                    System.Diagnostics.Debug.WriteLine($"Hit Char: '{hit.Text}' at index {hit.CharIndex} on page {i + 1}");
                }
                break;
            }
        }
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
            control.RefreshLayout();
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
            TotalPages = _currentDocument.PageCount;
            CurrentPage = 1;
            
            _objectManager.Clear();
            RefreshLayout();
            System.Diagnostics.Debug.WriteLine($"Successfully loaded PDF via Skia: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi mở file PDF: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void RefreshLayout()
    {
        if (_currentDocument == null) return;

        _pageRects.Clear();
        _pageCache.Values.ToList().ForEach(b => b.Dispose());
        _pageCache.Clear();

        double currentY = 0;
        double maxWidth = 0;
        float scale = (float)ZoomLevel;

        for (int i = 0; i < _currentDocument.PageCount; i++)
        {
            var pageSize = _currentDocument.Pages[i].Size;
            double w = pageSize.Width * scale;
            double h = pageSize.Height * scale;
            
            _pageRects.Add(new Rect(0, currentY, w, h));
            currentY += h + 20; // 20px spacing
            maxWidth = Math.Max(maxWidth, w);
        }

        skiaCanvas.Width = maxWidth;
        skiaCanvas.Height = currentY;
        
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
        if (_currentDocument == null) return;

        var canvas = e.Surface.Canvas;
        canvas.Clear(SKColors.DimGray);

        float scale = (float)ZoomLevel;
        
        double viewTop = PagesScrollViewer.VerticalOffset;
        double viewBottom = viewTop + PagesScrollViewer.ViewportHeight;

        for (int i = 0; i < _pageRects.Count; i++)
        {
            var rect = _pageRects[i];
            
            if (rect.Bottom >= viewTop && rect.Top <= viewBottom)
            {
                // Ensure page is mapped for hit testing
                _objectManager.MapPage(_currentDocument.Pages[i], i);

                if (!_pageCache.ContainsKey(i))
                {
                    _pageCache[i] = _renderEngine.RenderPage(_currentDocument.Pages[i], scale);
                }

                var bitmap = _pageCache[i];
                canvas.DrawBitmap(bitmap, (float)rect.Left, (float)rect.Top);
                
                using var paint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
                canvas.DrawRect((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, paint);
            }
            else
            {
                if (_pageCache.Count > 10 && _pageCache.ContainsKey(i))
                {
                    _pageCache[i].Dispose();
                    _pageCache.Remove(i);
                }
            }
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
                skiaCanvas.MouseDown -= OnCanvasMouseDown;
                DisposeCurrentDocument();
                _renderEngine.Dispose();
            }
            _disposed = true;
        }
    }
}