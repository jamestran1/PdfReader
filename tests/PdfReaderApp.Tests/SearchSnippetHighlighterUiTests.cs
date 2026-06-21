using System;
using System.Linq;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using PdfReaderApp;

namespace PdfReaderApp.Tests;

/// <summary>
/// Kiểm tra attached property của SearchSnippetHighlighter thực sự bơm Inlines
/// vào TextBlock (chạy trên STA vì cần đối tượng WPF).
/// </summary>
public class SearchSnippetHighlighterUiTests
{
    private static void RunSta(Action action)
    {
        Exception? captured = null;
        var t = new Thread(() =>
        {
            try { action(); }
            catch (Exception e) { captured = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (captured != null) throw captured;
    }

    [Fact]
    public void SettingTextAndQuery_PopulatesInlines_WithBoldMatch()
    {
        RunSta(() =>
        {
            var tb = new TextBlock();
            SearchSnippetHighlighter.SetText(tb, "ngài đi kinh hành và làm việc");
            SearchSnippetHighlighter.SetQuery(tb, "kinh hành");

            Assert.True(tb.Inlines.Count >= 1, "Inlines should be populated");
            Assert.Contains(tb.Inlines.OfType<Run>(),
                r => r.FontWeight == System.Windows.FontWeights.Bold && r.Text == "kinh hành");
        });
    }

    [Fact]
    public void BindingAttachedText_ViaDataContext_PopulatesInlines()
    {
        RunSta(() =>
        {
            var tb = new TextBlock { DataContext = new PdfReaderApp.Models.SearchResult(0, "ngài đi kinh hành và làm việc", 1) };
            BindingOperations.SetBinding(tb, SearchSnippetHighlighter.TextProperty, new Binding("Snippet"));
            SearchSnippetHighlighter.SetQuery(tb, "kinh hành");

            string rendered = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
            Assert.Equal("ngài đi kinh hành và làm việc", rendered);
        });
    }

    public sealed record VmStub(string Snippet, string Q);

    private static T? FindVisualChild<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < n; i++)
        {
            var c = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (c is T hit) return hit;
            var deeper = FindVisualChild<T>(c);
            if (deeper != null) return deeper;
        }
        return null;
    }

    [Fact]
    public void Reproduce_TemplatedBinding_RendersSnippet()
    {
        RunSta(() =>
        {
            // Mirror the popup DataTemplate: attached props delivered by XAML bindings inside a template.
            string xaml =
                "<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' " +
                "xmlns:local='clr-namespace:PdfReaderApp;assembly=PdfReaderApp'>" +
                "<TextBlock local:SearchSnippetHighlighter.Text='{Binding Snippet}' " +
                "local:SearchSnippetHighlighter.Query='{Binding Q}'/>" +
                "</DataTemplate>";
            var template = (System.Windows.DataTemplate)System.Windows.Markup.XamlReader.Parse(xaml);

            var cc = new ContentControl
            {
                ContentTemplate = template,
                Content = new VmStub("ngài đi ký sinh trùng ở đây", "ký sinh trùng")
            };
            cc.Measure(new System.Windows.Size(460, 200));
            cc.Arrange(new System.Windows.Rect(0, 0, 460, 200));
            cc.UpdateLayout();

            var tb = FindVisualChild<TextBlock>(cc);
            Assert.NotNull(tb);
            string rendered = string.Concat(tb!.Inlines.OfType<Run>().Select(r => r.Text));
            Assert.Equal("ngài đi ký sinh trùng ở đây", rendered);
        });
    }

    [Fact]
    public void BindingBothAttachedProps_ViaBindings_PopulatesInlines()
    {
        RunSta(() =>
        {
            var tb = new TextBlock { DataContext = new VmStub("ngài đi kinh hành và làm việc", "kinh hành") };
            // Replicate the popup: BOTH attached props delivered by Binding.
            BindingOperations.SetBinding(tb, SearchSnippetHighlighter.TextProperty, new Binding("Snippet"));
            BindingOperations.SetBinding(tb, SearchSnippetHighlighter.QueryProperty, new Binding("Q"));

            string rendered = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
            Assert.Equal("ngài đi kinh hành và làm việc", rendered);
            Assert.Contains(tb.Inlines.OfType<Run>(),
                r => r.FontWeight == System.Windows.FontWeights.Bold && r.Text == "kinh hành");
        });
    }

    [Fact]
    public void SettingOnlyText_StillRendersFullText()
    {
        RunSta(() =>
        {
            var tb = new TextBlock();
            SearchSnippetHighlighter.SetText(tb, "đoạn snippet không có query");

            string rendered = string.Concat(tb.Inlines.OfType<Run>().Select(r => r.Text));
            Assert.Equal("đoạn snippet không có query", rendered);
        });
    }
}
