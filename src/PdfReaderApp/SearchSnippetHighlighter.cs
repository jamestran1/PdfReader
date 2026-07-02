using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PdfReaderApp.Services;

namespace PdfReaderApp;

public static class SearchSnippetHighlighter
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string),
            typeof(SearchSnippetHighlighter), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.RegisterAttached("Query", typeof(string),
            typeof(SearchSnippetHighlighter), new PropertyMetadata(null, OnChanged));

    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);
    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetQuery(DependencyObject o, string v) => o.SetValue(QueryProperty, v);
    public static string GetQuery(DependencyObject o) => (string)o.GetValue(QueryProperty);

    private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not TextBlock tb) return;
        string text = GetText(tb) ?? "";
        string query = GetQuery(tb) ?? "";
        tb.Inlines.Clear();
        foreach (var (segText, isMatch) in SnippetHighlightComputer.ComputeSegments(text, query))
        {
            var run = new Run(segText);
            if (isMatch) run.FontWeight = FontWeights.Bold;
            tb.Inlines.Add(run);
        }
    }
}
