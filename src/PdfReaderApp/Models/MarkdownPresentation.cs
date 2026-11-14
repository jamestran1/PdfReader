using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace PdfReaderApp.Models;

/// <summary>Seam thuần để Chat và Note dùng chung mô hình hiển thị inline từ Markdown.</summary>
public static class MarkdownPresentation
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

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
            switch (block)
            {
                case Table table:
                    blocks.Add(CreateTable(table));
                    break;
                case ListBlock list when list.Descendants<TaskList>().Any():
                    blocks.Add(CreateTaskList(list));
                    break;
            }
        }

        return blocks;
    }

    private static MarkdownTableBlock CreateTable(Table table)
    {
        var alignments = table.ColumnDefinitions
            .Select(definition => definition.Alignment switch
            {
                TableColumnAlign.Left => MarkdownTableAlignment.Left,
                TableColumnAlign.Center => MarkdownTableAlignment.Center,
                TableColumnAlign.Right => MarkdownTableAlignment.Right,
                _ => MarkdownTableAlignment.None,
            })
            .ToArray();
        var header = new List<IReadOnlyList<MarkdownInlineSpan>>();
        var rows = new List<IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>>>();

        foreach (TableRow tableRow in table.OfType<TableRow>())
        {
            IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>> cells = tableRow
                .OfType<TableCell>()
                .Select(CreateCell)
                .ToArray();
            if (tableRow.IsHeader) header.AddRange(cells);
            else rows.Add(cells);
        }

        return new MarkdownTableBlock(alignments.Take(header.Count).ToArray(), header, rows);
    }

    private static IReadOnlyList<MarkdownInlineSpan> CreateCell(TableCell cell) =>
        cell.Descendants<LeafBlock>()
            .Where(block => block.Inline is not null)
            .SelectMany(block => CreateSpans(block.Inline!))
            .ToArray();

    private static MarkdownTaskListBlock CreateTaskList(ListBlock list)
    {
        var items = new List<MarkdownTaskListItem>();
        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            TaskList? marker = item.Descendants<TaskList>().FirstOrDefault();
            ContainerInline? parent = marker?.Parent;
            if (marker is null || parent is null) continue;

            var spans = new List<MarkdownInlineSpan>();
            Visit(parent, MarkdownInlineStyles.None, null, spans);
            if (spans.Count > 0 && char.IsWhiteSpace(spans[0].Text[0]))
                spans[0] = spans[0] with { Text = spans[0].Text.TrimStart() };

            items.Add(new MarkdownTaskListItem(marker.Checked, spans));
        }

        return new MarkdownTaskListBlock(items);
    }

    private static List<MarkdownInlineSpan> CreateSpans(ContainerInline container)
    {
        var spans = new List<MarkdownInlineSpan>();
        Visit(container, MarkdownInlineStyles.None, null, spans);
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
                    Visit(emphasis, styles | EmphasisStyle(emphasis), url, spans);
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

    private static MarkdownInlineStyles EmphasisStyle(EmphasisInline emphasis) =>
        emphasis.DelimiterChar == '~'
            ? MarkdownInlineStyles.Strikethrough
            : EmphasisStyle(emphasis.DelimiterCount);

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
