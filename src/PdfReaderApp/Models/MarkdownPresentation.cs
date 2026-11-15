using System.Text;
using System.Globalization;
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
        var spans = new List<MarkdownInlineSpan>();
        foreach (Block block in document.Descendants<Block>())
        {
            ContainerInline? inline = block switch
            {
                HeadingBlock heading => heading.Inline,
                ParagraphBlock paragraph => paragraph.Inline,
                _ => null,
            };
            if (inline is null) continue;
            if (spans.Count > 0) spans.Add(new MarkdownInlineSpan("\n\n"));
            var writer = new InlineWriter(citations, spans);
            writer.Visit(inline);
        }

        return spans;
    }

    public static IReadOnlyList<MarkdownBlock> ParseBlocks(string markdown)
    {
        return ParseBlocks(markdown, []);
    }

    public static IReadOnlyList<MarkdownBlock> ParseBlocks(
        string markdown,
        IReadOnlyList<RagSource> sources,
        Func<RagSource, string>? citationLabel = null)
    {
        (string protectedMarkdown, var citations) = ProtectCitations(markdown, sources, citationLabel);
        MarkdownDocument document = Markdown.Parse(protectedMarkdown, Pipeline);
        var blocks = new List<MarkdownBlock>();
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
                case HeadingBlock heading when heading.Inline is not null:
                    blocks.Add(new MarkdownPresentationBlock(
                        MarkdownPresentationBlockKind.Heading,
                        CreateSpans(heading.Inline, citations),
                        heading.Level));
                    break;
                case ListBlock list:
                    ParseList(list, 0, blocks, citations);
                    break;
                case QuoteBlock quote:
                    foreach (ParagraphBlock paragraph in quote.Descendants<ParagraphBlock>())
                    {
                        if (paragraph.Inline is null) continue;
                        blocks.Add(new MarkdownPresentationBlock(
                            MarkdownPresentationBlockKind.Quote,
                            CreateSpans(paragraph.Inline, citations)));
                    }
                    break;
                case FencedCodeBlock code:
                    blocks.Add(new MarkdownPresentationBlock(
                        MarkdownPresentationBlockKind.Code,
                        [],
                        Text: NormalizeNewlines(code.Lines.ToString()).TrimEnd('\n'),
                        IsComplete: code.ClosingFencedCharCount > 0,
                        Language: code.Info ?? string.Empty));
                    break;
                case ParagraphBlock paragraph when paragraph.Inline is not null:
                    blocks.Add(new MarkdownPresentationBlock(
                        MarkdownPresentationBlockKind.Paragraph,
                        CreateSpans(paragraph.Inline, citations)));
                    break;
            }
        }

        return blocks;
    }

    private static void ParseList(
        ListBlock list,
        int level,
        List<MarkdownBlock> blocks,
        IReadOnlyDictionary<string, CitationSpan> citations)
    {
        int number = int.TryParse(list.OrderedStart, NumberStyles.Integer, CultureInfo.InvariantCulture, out int orderedStart)
            ? orderedStart
            : 1;
        foreach (ListItemBlock item in list.OfType<ListItemBlock>())
        {
            blocks.Add(new MarkdownPresentationBlock(
                MarkdownPresentationBlockKind.ListItem,
                ParseItemText(item, citations),
                level,
                list.IsOrdered ? $"{number}." : "•"));
            foreach (ListBlock nestedList in item.OfType<ListBlock>())
                ParseList(nestedList, level + 1, blocks, citations);
            if (list.IsOrdered) number++;
        }
    }

    private static List<MarkdownInlineSpan> ParseItemText(
        ListItemBlock item,
        IReadOnlyDictionary<string, CitationSpan> citations)
    {
        ParagraphBlock? paragraph = item.OfType<ParagraphBlock>().FirstOrDefault();
        return paragraph?.Inline is null ? [] : CreateSpans(paragraph.Inline, citations);
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

    private static MarkdownInlineSpan[] CreateCell(
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
            new InlineWriter(citations, spans).Visit(parent);
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
        new InlineWriter(citations, spans).Visit(container);
        return spans;
    }

    private static (string Markdown, Dictionary<string, CitationSpan> Citations) ProtectCitations(
        string markdown,
        IReadOnlyList<RagSource> sources,
        Func<RagSource, string>? citationLabel = null)
    {
        if (sources.Count == 0) return (markdown, []);

        var builder = new StringBuilder();
        var citations = new Dictionary<string, CitationSpan>(StringComparer.Ordinal);
        int index = 0;
        foreach ((string segment, bool isFencedCode) in SplitOutsideFencedCode(markdown))
        {
            if (isFencedCode)
            {
                builder.Append(segment);
            }
            else
            {
                foreach (CitationSpan citation in CitationParser.Parse(segment, sources))
                {
                    if (citation.Source is not { } source)
                    {
                        builder.Append(citation.Text);
                        continue;
                    }

                    string placeholder = $"{PlaceholderStart}{index++}{PlaceholderEnd}";
                    citations.Add(placeholder, new CitationSpan(
                        citationLabel?.Invoke(source) ?? citation.Text, source, citation.Quote));
                    builder.Append(placeholder);
                }
            }
        }

        RemoveMarkdownLinkDestinations(builder, citations);
        return (builder.ToString(), citations);
    }

    private static IEnumerable<(string Text, bool IsFencedCode)> SplitOutsideFencedCode(string markdown)
    {
        var current = new StringBuilder();
        bool inFencedCode = false;
        char fenceChar = '\0';
        int fenceLength = 0;
        int lineStart = 0;

        while (lineStart < markdown.Length)
        {
            int lineEnd = markdown.IndexOf('\n', lineStart);
            int nextLineStart = lineEnd < 0 ? markdown.Length : lineEnd + 1;
            string line = markdown[lineStart..nextLineStart];

            if (!inFencedCode && TryGetOpeningFence(line, out char openingChar, out int openingLength))
            {
                yield return (current.ToString(), false);
                current.Clear();
                inFencedCode = true;
                fenceChar = openingChar;
                fenceLength = openingLength;
                current.Append(line);
            }
            else
            {
                current.Append(line);
                if (inFencedCode && IsClosingFence(line, fenceChar, fenceLength))
                {
                    yield return (current.ToString(), true);
                    current.Clear();
                    inFencedCode = false;
                }
            }

            lineStart = nextLineStart;
        }

        yield return (current.ToString(), inFencedCode);
    }

    private static bool TryGetOpeningFence(string line, out char fenceChar, out int fenceLength)
    {
        fenceChar = '\0';
        fenceLength = 0;
        int start = CountLeadingSpaces(line);
        if (start >= line.Length) return false;

        char candidate = line[start];
        if (candidate is not ('`' or '~')) return false;

        int end = start;
        while (end < line.Length && line[end] == candidate) end++;
        fenceLength = end - start;
        if (fenceLength < 3) return false;

        string info = line[end..].TrimEnd('\r');
        if (candidate == '`' && info.Contains('`')) return false;

        fenceChar = candidate;
        return true;
    }

    private static bool IsClosingFence(string line, char fenceChar, int fenceLength)
    {
        int start = CountLeadingSpaces(line);
        int end = start;
        while (end < line.Length && line[end] == fenceChar) end++;
        return end - start >= fenceLength && line[end..].TrimEnd('\r', ' ', '\t').Length == 0;
    }

    private static int CountLeadingSpaces(string line)
    {
        int start = 0;
        while (start < line.Length && start < 3 && line[start] == ' ') start++;
        return start;
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

    private sealed class InlineWriter(
        IReadOnlyDictionary<string, CitationSpan> citations,
        List<MarkdownInlineSpan> spans)
    {
        private MarkdownInlineStyles Styles { get; set; } = MarkdownInlineStyles.None;
        private string? Url { get; set; }

        public void Visit(ContainerInline container)
        {
            foreach (Inline inline in container)
            {
                switch (inline)
                {
                    case LiteralInline literal:
                        Add(literal.Content.ToString());
                        break;
                    case HtmlEntityInline entity:
                        Add(entity.Transcoded.ToString());
                        break;
                    case CodeInline code:
                        RunWith(Styles | MarkdownInlineStyles.Code, null, () => Add(code.Content.ToString()));
                        break;
                    case EmphasisInline emphasis:
                        RunWith(Styles | EmphasisStyle(emphasis), Url, () => Visit(emphasis));
                        break;
                    case LinkInline { IsImage: false } link:
                        RunWith(Styles, SafeExternalUrl(link.Url), () => Visit(link));
                        break;
                    case AutolinkInline autolink:
                        Add(autolink.Url);
                        break;
                    case LineBreakInline:
                        Add("\n");
                        break;
                }
            }
        }

        private void RunWith(MarkdownInlineStyles styles, string? url, Action action)
        {
            MarkdownInlineStyles previousStyles = Styles;
            string? previousUrl = Url;
            Styles = styles;
            Url = url;
            try { action(); }
            finally
            {
                Styles = previousStyles;
                Url = previousUrl;
            }
        }

        private void Add(string text)
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
                        FlushPlain(plain);
                        spans.Add(new MarkdownInlineSpan(citation.Text, Styles, Citation: citation));
                    }
                }

                cursor = match.Index + match.Length;
            }

            if (cursor < text.Length) plain.Append(text[cursor..]);
            FlushPlain(plain);
        }

        private void FlushPlain(StringBuilder plain)
        {
            if (plain.Length > 0)
            {
                string text = plain.ToString();
                if (spans.Count > 0 &&
                    spans[^1] is { Citation: null, Styles: var lastStyles, Url: var lastUrl } &&
                    lastStyles == Styles &&
                    lastUrl == Url &&
                    (spans[^1].Text.EndsWith('[') || text.StartsWith('[')))
                {
                    spans[^1] = spans[^1] with { Text = spans[^1].Text + text };
                }
                else
                {
                    spans.Add(new MarkdownInlineSpan(text, Styles, Url));
                }
            }
            plain.Clear();
        }
    }

    private static MarkdownInlineStyles EmphasisStyle(int delimiterCount) =>
        delimiterCount >= 2 ? MarkdownInlineStyles.Bold : MarkdownInlineStyles.Italic;

    private static MarkdownInlineStyles EmphasisStyle(EmphasisInline emphasis) =>
        emphasis.DelimiterChar == '~'
            ? MarkdownInlineStyles.Strikethrough
            : EmphasisStyle(emphasis.DelimiterCount);

    private static string NormalizeNewlines(string text) => text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string? SafeExternalUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) &&
        (parsed.Scheme == Uri.UriSchemeHttp || parsed.Scheme == Uri.UriSchemeHttps)
            ? parsed.AbsoluteUri
            : null;

    [GeneratedRegex(@"\uE000\d+\uE001", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex PlaceholderRegex();

    [GeneratedRegex(@"(?<placeholder>\uE000\d+\uE001)(?<destination>\([^)\r\n]*\))", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 200)]
    private static partial Regex MarkdownLinkDestination();
}
