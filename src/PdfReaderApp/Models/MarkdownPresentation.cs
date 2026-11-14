using System.Globalization;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
namespace PdfReaderApp.Models;

/// <summary>Seam thuần để Chat và Note dùng chung mô hình hiển thị inline từ Markdown.</summary>
public static class MarkdownPresentation
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();

    public static IReadOnlyList<MarkdownInlineSpan> ParseInline(string markdown)
    {
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        var paragraph = document.Descendants<ParagraphBlock>().FirstOrDefault();
        if (paragraph?.Inline is null) return [];

        var spans = new List<MarkdownInlineSpan>();
        Visit(paragraph.Inline, MarkdownInlineStyles.None, null, spans);
        return spans;
    }

    public static IReadOnlyList<MarkdownPresentationBlock> ParseBlocks(string markdown)
    {
        MarkdownDocument document = Markdown.Parse(markdown, Pipeline);
        var blocks = new List<MarkdownPresentationBlock>();
        foreach (Block block in document)
        {
            if (block is HeadingBlock heading && heading.Inline is not null)
            {
                var spans = new List<MarkdownInlineSpan>();
                Visit(heading.Inline, MarkdownInlineStyles.None, null, spans);
                blocks.Add(new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.Heading,
                    spans,
                    heading.Level));
            }
            else if (block is ListBlock list)
            {
                ParseList(list, 0, blocks);
            }
            else if (block is QuoteBlock quote)
            {
                foreach (ParagraphBlock paragraph in quote.Descendants<ParagraphBlock>())
                {
                    if (paragraph.Inline is null) continue;

                    var spans = new List<MarkdownInlineSpan>();
                    Visit(paragraph.Inline, MarkdownInlineStyles.None, null, spans);
                    blocks.Add(new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Quote, spans));
                }
            }
            else if (block is FencedCodeBlock code)
            {
                blocks.Add(new MarkdownPresentationBlock(
                    MarkdownPresentationBlockKind.Code,
                    [],
                    Text: NormalizeNewlines(code.Lines.ToString()).TrimEnd('\n'),
                    IsComplete: code.ClosingFencedCharCount > 0));
            }
            else if (block is ParagraphBlock paragraph && paragraph.Inline is not null)
            {
                var spans = new List<MarkdownInlineSpan>();
                Visit(paragraph.Inline, MarkdownInlineStyles.None, null, spans);
                blocks.Add(new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Paragraph, spans));
            }
        }

        return blocks;
    }

    private static void ParseList(ListBlock list, int level, List<MarkdownPresentationBlock> blocks)
    {
        int number = int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int orderedStart)
            ? orderedStart
            : 1;
        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            blocks.Add(new MarkdownPresentationBlock(
                MarkdownPresentationBlockKind.ListItem,
                ParseItemText(item),
                level,
                list.IsOrdered ? $"{number}." : "•"));
            foreach (ListBlock nestedList in item.OfType<ListBlock>()) ParseList(nestedList, level + 1, blocks);
            if (list.IsOrdered) number++;
        }
    }

    private static List<MarkdownInlineSpan> ParseItemText(ListItemBlock item)
    {
        var paragraph = item.OfType<ParagraphBlock>().FirstOrDefault();
        if (paragraph?.Inline is null) return [];

        var spans = new List<MarkdownInlineSpan>();
        Visit(paragraph.Inline, MarkdownInlineStyles.None, null, spans);
        return spans;
    }

    private static void Visit(
        ContainerInline container,
        MarkdownInlineStyles styles,
        string? url,
        List<MarkdownInlineSpan> spans)
    {
        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    Add(literal.Content.ToString(), styles, url, spans);
                    break;
                case CodeInline code:
                    Add(code.Content.ToString(), styles | MarkdownInlineStyles.Code, null, spans);
                    break;
                case EmphasisInline emphasis:
                    Visit(emphasis, styles | EmphasisStyle(emphasis.DelimiterCount), url, spans);
                    break;
                case LinkInline { IsImage: false } link:
                    string? safeUrl = SafeExternalUrl(link.Url);
                    Visit(link, styles, safeUrl, spans);
                    break;
                case AutolinkInline autolink:
                    Add(autolink.Url, styles, SafeExternalUrl(autolink.Url), spans);
                    break;
            }
        }
    }

    private static MarkdownInlineStyles EmphasisStyle(int delimiterCount) =>
        delimiterCount >= 2 ? MarkdownInlineStyles.Bold : MarkdownInlineStyles.Italic;

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string? SafeExternalUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed.AbsoluteUri
            : null;

    private static void Add(
        string text,
        MarkdownInlineStyles styles,
        string? url,
        List<MarkdownInlineSpan> spans)
    {
        if (text.Length > 0) spans.Add(new MarkdownInlineSpan(text, styles, url));
    }
}
