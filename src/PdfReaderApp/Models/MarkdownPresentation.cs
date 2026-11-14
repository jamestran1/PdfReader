using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Seam thuần để Chat và Note dùng chung mô hình hiển thị inline từ Markdown.</summary>
public static partial class MarkdownPresentation
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UsePipeTables()
        .UseTaskLists()
        .UseAutoLinks()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();
    private const char PlaceholderStart = '\uE000';
    private const char PlaceholderEnd = '\uE001';

    public static IReadOnlyList<MarkdownInlineSpan> ParseInline(string markdown) => ParseInline(markdown, []);

    public static IReadOnlyList<MarkdownInlineSpan> ParseInline(string markdown, IReadOnlyList<RagSource> sources)
    {
        (string protectedMarkdown, var citations) = ProtectCitations(markdown, sources);
        MarkdownDocument document = Markdown.Parse(protectedMarkdown, Pipeline);
        var inline = FirstInline(document);
        if (inline is null) return [];

        var spans = new List<MarkdownInlineSpan>();
        Visit(inline, MarkdownInlineStyles.None, null, citations, spans);
        return spans;
    }

    public static IReadOnlyList<MarkdownPresentationBlock> ParseBlocks(string markdown)
    {
        return ParseBlocks(markdown, []);
    }

    public static IReadOnlyList<MarkdownPresentationBlock> ParseBlocks(
        string markdown,
        IReadOnlyList<RagSource> sources)
    {
        (string protectedMarkdown, var citations) = ProtectCitations(markdown, sources);
        MarkdownDocument document = Markdown.Parse(protectedMarkdown, Pipeline);
        var blocks = new List<MarkdownPresentationBlock>();
        foreach (Block block in document)
        {
            switch (block)
            {
                case Table table:
                    blocks.Add(CreateTable(table, citations));
                    break;
                case ListBlock list when list.Descendants<TaskList>().Any():
                    blocks.Add(CreateTaskList(list, citations));
                    break;
            }
        }

        return blocks;
    }

    private static MarkdownTableBlock CreateTable(Table table, IReadOnlyDictionary<string, CitationSpan> citations)
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
                .Select(cell => CreateCell(cell, citations))
                .ToArray();
            if (tableRow.IsHeader) header.AddRange(cells);
            else rows.Add(cells);
        }

        return new MarkdownTableBlock(alignments.Take(header.Count).ToArray(), header, rows);
    }

    private static IReadOnlyList<MarkdownInlineSpan> CreateCell(
        TableCell cell,
        IReadOnlyDictionary<string, CitationSpan> citations) =>
        cell.Descendants<LeafBlock>()
            .Where(block => block.Inline is not null)
            .SelectMany(block => CreateSpans(block.Inline!, citations))
            .ToArray();

    private static MarkdownTaskListBlock CreateTaskList(
        ListBlock list,
        IReadOnlyDictionary<string, CitationSpan> citations)
    {
        var items = new List<MarkdownTaskListItem>();
        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            TaskList? marker = item.Descendants<TaskList>().FirstOrDefault();
            ContainerInline? parent = marker?.Parent;
            if (marker is null || parent is null) continue;

            var spans = new List<MarkdownInlineSpan>();
            Visit(parent, MarkdownInlineStyles.None, null, citations, spans);
            if (spans.Count > 0 && char.IsWhiteSpace(spans[0].Text[0]))
                spans[0] = spans[0] with { Text = spans[0].Text.TrimStart() };

            items.Add(new MarkdownTaskListItem(marker.Checked, spans));
        }

        return new MarkdownTaskListBlock(items);
    }

    private static List<MarkdownInlineSpan> CreateSpans(
        ContainerInline container,
        IReadOnlyDictionary<string, CitationSpan> citations)
    {
        var spans = new List<MarkdownInlineSpan>();
        Visit(container, MarkdownInlineStyles.None, null, citations, spans);
        return spans;
    }

    private static (string Markdown, Dictionary<string, CitationSpan> Citations) ProtectCitations(
        string markdown,
        IReadOnlyList<RagSource> sources)
    {
        if (sources.Count == 0) return (markdown, []);

        var builder = new StringBuilder();
        var citations = new Dictionary<string, CitationSpan>(StringComparer.Ordinal);
        int index = 0;
        foreach (CitationSpan citation in CitationParser.Parse(markdown, sources))
        {
            if (citation.Source is null)
            {
                ProtectPlainBrackets(citation.Text, citations, builder, ref index);
                continue;
            }

            string placeholder = $"{PlaceholderStart}{index++}{PlaceholderEnd}";
            citations.Add(placeholder, citation);
            builder.Append(placeholder);
        }

        RemoveMarkdownLinkDestinations(builder, citations);
        return (builder.ToString(), citations);
    }

    private static void ProtectPlainBrackets(
        string text,
        Dictionary<string, CitationSpan> citations,
        StringBuilder builder,
        ref int index)
    {
        int start = -1;
        int cursor = 0;
        for (int position = 0; position < text.Length; position++)
        {
            if (text[position] == '[')
            {
                if (start < 0) start = position;
            }
            else if (text[position] == ']' && start >= 0)
            {
                builder.Append(text[cursor..start]);
                string bracketText = text[start..(position + 1)];
                string placeholder = $"{PlaceholderStart}{index++}{PlaceholderEnd}";
                citations.Add(placeholder, new CitationSpan(bracketText));
                builder.Append(placeholder);
                cursor = position + 1;
                start = -1;
            }
        }

        builder.Append(text[cursor..]);
    }

    private static void RemoveMarkdownLinkDestinations(
        StringBuilder builder,
        Dictionary<string, CitationSpan> citations)
    {
        foreach (Match match in MarkdownLinkDestination().Matches(builder.ToString()))
        {
            if (citations.TryGetValue(match.Groups["placeholder"].Value, out CitationSpan? citation) &&
                citation.Source is not null)
            {
                Group destination = match.Groups["destination"];
                builder.Remove(destination.Index, destination.Length);
            }
        }
    }

    private static ContainerInline? FirstInline(ContainerBlock blocks)
    {
        foreach (Block block in blocks)
        {
            ContainerInline? inline = block switch
            {
                HeadingBlock heading => heading.Inline,
                ParagraphBlock paragraph => paragraph.Inline,
                ContainerBlock container => FirstInline(container),
                _ => null,
            };
            if (inline is not null) return inline;
        }

        return null;
    }

    private static void Visit(
        ContainerInline container,
        MarkdownInlineStyles styles,
        string? url,
        IReadOnlyDictionary<string, CitationSpan> citations,
        List<MarkdownInlineSpan> spans)
    {
        foreach (Inline inline in container)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    Add(literal.Content.ToString(), styles, url, citations, spans);
                    break;
                case CodeInline code:
                    Add(code.Content.ToString(), styles | MarkdownInlineStyles.Code, null, citations, spans);
                    break;
                case EmphasisInline emphasis:
                    Visit(emphasis, styles | EmphasisStyle(emphasis), url, citations, spans);
                    break;
                case LinkInline { IsImage: false } link:
                    string? safeUrl = SafeExternalUrl(link.Url);
                    Visit(link, styles, safeUrl, citations, spans);
                    break;
                case AutolinkInline autolink:
                    Add(autolink.Url, styles, SafeExternalUrl(autolink.Url), citations, spans);
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
        IReadOnlyDictionary<string, CitationSpan> citations,
        List<MarkdownInlineSpan> spans)
    {
        int cursor = 0;
        var plain = new StringBuilder();
        foreach (Match match in PlaceholderRegex().Matches(text))
        {
            if (match.Index > cursor) plain.Append(text[cursor..match.Index]);

            if (citations.TryGetValue(match.Value, out CitationSpan? citation))
            {
                if (citation.Source is null)
                {
                    plain.Append(citation.Text);
                }
                else
                {
                    FlushPlain(plain, styles, url, spans);
                    spans.Add(new MarkdownInlineSpan(citation.Text, styles, Citation: citation));
                }
            }

            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length) plain.Append(text[cursor..]);
        FlushPlain(plain, styles, url, spans);
    }

    private static void FlushPlain(
        StringBuilder plain,
        MarkdownInlineStyles styles,
        string? url,
        List<MarkdownInlineSpan> spans)
    {
        if (plain.Length > 0) spans.Add(new MarkdownInlineSpan(plain.ToString(), styles, url));
        plain.Clear();
    }

    [GeneratedRegex(@"\uE000\d+\uE001", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"(?<placeholder>\uE000\d+\uE001)(?<destination>\([^)\r\n]*\))", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex MarkdownLinkDestination();
}
