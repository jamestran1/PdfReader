using System.Text;
using System.Text.RegularExpressions;
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Seam thuần để Chat và Note dùng chung mô hình hiển thị inline từ Markdown.</summary>
public static partial class MarkdownPresentation
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder().Build();
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
                    Visit(emphasis, styles | EmphasisStyle(emphasis.DelimiterCount), url, citations, spans);
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
