using System;
using System.Windows;
using System.Windows.Controls;
using PdfiumViewer.Core; // For PdfDocument

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

    public PdfViewerControl()
    {
        InitializeComponent();
        this.Unloaded += PdfViewerControl_Unloaded;
    }

    private static void OnDocumentSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is PdfViewerControl control && e.NewValue is string path && !string.IsNullOrEmpty(path))
        {
            control.LoadDocument(path);
        }
    }

    private void LoadDocument(string path)
    {
        try
        {
            // Giải phóng tài liệu cũ trước khi mở tài liệu mới
            DisposeCurrentDocument();

            _currentDocument = PdfDocument.Load(path);
            pdfViewer.Document = _currentDocument;
            
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
            // Set Document of renderer to null to detach it
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
                DisposeCurrentDocument();
            }
            _disposed = true;
        }
    }
}