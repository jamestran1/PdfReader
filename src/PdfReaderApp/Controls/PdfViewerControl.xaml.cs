using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using PdfiumViewer.Core;

namespace PdfReaderApp.Controls;

public partial class PdfViewerControl : UserControl, IDisposable
{
    private PdfDocument? _currentDocument;
    private bool _disposed;

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

        // Hook into internal PDFViewer property changes
        var pageDescriptor = DependencyPropertyDescriptor.FromProperty(PdfiumViewer.PDFViewer.PageProperty, typeof(PdfiumViewer.PDFViewer));
        pageDescriptor?.AddValueChanged(pdfViewer, OnPdfViewerPageChanged);

        var zoomDescriptor = DependencyPropertyDescriptor.FromProperty(PdfiumViewer.PDFViewer.ZoomProperty, typeof(PdfiumViewer.PDFViewer));
        zoomDescriptor?.AddValueChanged(pdfViewer, OnPdfViewerZoomChanged);
    }

    private void OnPdfViewerPageChanged(object? sender, EventArgs e)
    {
        // Internal page is 0-based
        int newPage = pdfViewer.Page + 1;
        if (CurrentPage != newPage)
        {
            CurrentPage = newPage;
        }
    }

    private void OnPdfViewerZoomChanged(object? sender, EventArgs e)
    {
        double newZoom = pdfViewer.Zoom;
        if (Math.Abs(ZoomLevel - newZoom) > 0.01)
        {
            ZoomLevel = newZoom;
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
            int newPage = (int)e.NewValue - 1;
            if (control.pdfViewer.Page != newPage && newPage >= 0 && newPage < control.TotalPages)
            {
                control.pdfViewer.Page = newPage;
            }
        }
    }

    private static void OnZoomLevelChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && control._currentDocument != null)
        {
            double newZoom = (double)e.NewValue;
            if (Math.Abs(control.pdfViewer.Zoom - newZoom) > 0.01)
            {
                control.pdfViewer.Zoom = newZoom;
            }
        }
    }

    private void LoadDocument(string path)
    {
        try
        {
            DisposeCurrentDocument();

            _currentDocument = PdfDocument.Load(path);
            pdfViewer.Document = _currentDocument;
            
            TotalPages = _currentDocument.PageCount;
            CurrentPage = 1;
            ZoomLevel = pdfViewer.Zoom;

            System.Diagnostics.Debug.WriteLine($"Successfully loaded PDF: {path}");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Lỗi khi mở file PDF: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void DisposeCurrentDocument()
    {
        if (_currentDocument != null)
        {
            pdfViewer.Document = null;
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
                // Unhook descriptors
                var pageDescriptor = DependencyPropertyDescriptor.FromProperty(PdfiumViewer.PDFViewer.PageProperty, typeof(PdfiumViewer.PDFViewer));
                pageDescriptor?.RemoveValueChanged(pdfViewer, OnPdfViewerPageChanged);

                var zoomDescriptor = DependencyPropertyDescriptor.FromProperty(PdfiumViewer.PDFViewer.ZoomProperty, typeof(PdfiumViewer.PDFViewer));
                zoomDescriptor?.RemoveValueChanged(pdfViewer, OnPdfViewerZoomChanged);

                DisposeCurrentDocument();
            }
            _disposed = true;
        }
    }
}