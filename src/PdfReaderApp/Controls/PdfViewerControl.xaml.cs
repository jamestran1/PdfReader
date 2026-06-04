using System.Windows;
using System.Windows.Controls;
using PdfiumViewer;

namespace PdfReaderApp.Controls;

public partial class PdfViewerControl : UserControl
{
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
        // Logic render đơn giản: Hiển thị tên file để verify binding trước
        System.Diagnostics.Debug.WriteLine($"Loading PDF: {path}");
    }
}