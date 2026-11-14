using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using PdfReaderApp.Models;

namespace PdfReaderApp.Controls;

/// <summary>Renderer inline Markdown từ presentation model thuần; không bao giờ nhận HTML hay Markdown raw.</summary>
public sealed class MarkdownTextBlock : System.Windows.Controls.TextBlock
{
    public static readonly DependencyProperty InlineSpansProperty = DependencyProperty.Register(
        nameof(InlineSpans),
        typeof(IReadOnlyList<MarkdownInlineSpan>),
        typeof(MarkdownTextBlock),
        new PropertyMetadata(Array.Empty<MarkdownInlineSpan>(), OnInlineSpansChanged));

    public IReadOnlyList<MarkdownInlineSpan> InlineSpans
    {
        get => (IReadOnlyList<MarkdownInlineSpan>)GetValue(InlineSpansProperty);
        set => SetValue(InlineSpansProperty, value);
    }

    private static void OnInlineSpansChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var block = (MarkdownTextBlock)d;
        block.Inlines.Clear();
        foreach (MarkdownInlineSpan span in block.InlineSpans) block.Inlines.Add(CreateInline(span));
    }

    private static Inline CreateInline(MarkdownInlineSpan span)
    {
        var run = new Run(span.Text);
        if (span.Styles.HasFlag(MarkdownInlineStyles.Bold)) run.FontWeight = FontWeights.Bold;
        if (span.Styles.HasFlag(MarkdownInlineStyles.Italic)) run.FontStyle = FontStyles.Italic;
        if (span.Styles.HasFlag(MarkdownInlineStyles.Code)) run.FontFamily = new FontFamily("Consolas");

        if (span.Url is null) return run;

        var link = new Hyperlink(run)
        {
            NavigateUri = new Uri(span.Url, UriKind.Absolute),
            ToolTip = span.Url,
        };
        link.RequestNavigate += OpenExternalLink;
        link.SetResourceReference(TextElement.ForegroundProperty, "MaterialDesign.Brush.Primary");
        return link;
    }

    private static void OpenExternalLink(object sender, System.Windows.Navigation.RequestNavigateEventArgs e)
    {
        if (e.Uri.Scheme != Uri.UriSchemeHttp && e.Uri.Scheme != Uri.UriSchemeHttps) return;

        Process.Start(new ProcessStartInfo { FileName = e.Uri.AbsoluteUri, UseShellExecute = true });
    }
}
