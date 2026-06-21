using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PdfReaderApp.Services;

namespace PdfReaderApp;

/// <summary>
/// Tách snippet thành các đoạn thường/khớp (accent-insensitive) để in đậm từ khóa,
/// và expose attached property gắn vào TextBlock.
/// </summary>
public static class SearchSnippetHighlighter
{
    public static IReadOnlyList<(string Text, bool IsMatch)> ComputeSegments(string text, string query)
    {
        if (string.IsNullOrEmpty(text)) return new List<(string, bool)>();

        string fq = SearchNormalizer.Fold(query);
        if (fq.Length == 0) return new List<(string, bool)> { (text, false) };

        var (ft, map) = SearchNormalizer.FoldWithMap(text);
        var segs = new List<(string, bool)>();
        int srcPos = 0;
        int search = 0;
        while (search <= ft.Length - fq.Length)
        {
            int idx = ft.IndexOf(fq, search, StringComparison.Ordinal);
            if (idx < 0) break;

            int srcStart = map[idx];
            int srcEnd = map[idx + fq.Length - 1] + 1;
            if (srcStart < srcPos) srcStart = srcPos; // an toàn khi gộp whitespace

            if (srcStart > srcPos)
                segs.Add((text.Substring(srcPos, srcStart - srcPos), false));
            if (srcEnd > srcStart)
                segs.Add((text.Substring(srcStart, srcEnd - srcStart), true));

            srcPos = srcEnd;
            search = idx + fq.Length;
        }
        if (srcPos < text.Length)
            segs.Add((text.Substring(srcPos), false));

        return segs;
    }

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
        foreach (var (segText, isMatch) in ComputeSegments(text, query))
        {
            var run = new Run(segText);
            if (isMatch) run.FontWeight = FontWeights.Bold;
            tb.Inlines.Add(run);
        }
    }
}
