using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Models;

public sealed class MarkdownPresentationTests
{
    private static readonly IReadOnlyList<RagSource> Sources =
    [
        new(1, "docA", "Hợp đồng", 2),
        new(2, "docB", "Báo cáo", 0),
    ];

    [Fact]
    public void ParseInline_BuildsOnlySafeInlinePresentation()
    {
        const string markdown = """
            **Chat** *AI* `code` [Open](https://example.com/a?b=1) [Bad](javascript:alert(1))
            <script>alert(1)</script>[![logo](https://example.com/logo.png)](https://example.com)
            """;

        IReadOnlyList<MarkdownInlineSpan> spans = MarkdownPresentation.ParseInline(markdown);

        Assert.Equal(
        [
            new MarkdownInlineSpan("Chat", MarkdownInlineStyles.Bold),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("AI", MarkdownInlineStyles.Italic),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("code", MarkdownInlineStyles.Code),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("Open", MarkdownInlineStyles.None, "https://example.com/a?b=1"),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("Bad", MarkdownInlineStyles.None),
        ], spans);
    }

    [Fact]
    public void ParseInline_CitationGroupsKeepLinksAndUnknownFallback()
    {
        IReadOnlyList<MarkdownInlineSpan> spans = MarkdownPresentation.ParseInline(
            "Kết luận [1, 9][2] và [3].", Sources);

        Assert.Equal(
        [
            new MarkdownInlineSpan("Kết luận "),
            new MarkdownInlineSpan("📖 Hợp đồng - Tr.3", Citation: CitationSpan.ForSource(Sources[0])),
            new MarkdownInlineSpan("[9]"),
            new MarkdownInlineSpan("📖 Báo cáo - Tr.1", Citation: CitationSpan.ForSource(Sources[1])),
            new MarkdownInlineSpan(" và [3]."),
        ], spans);
    }

    [Theory]
    [InlineData("### **[1](https://example.com/source)**")]
    [InlineData("- **[1](https://example.com/source)**")]
    public void ParseInline_CitationInsideHeadingOrList_WinsExternalLink(string markdown)
    {
        IReadOnlyList<MarkdownInlineSpan> spans = MarkdownPresentation.ParseInline(markdown, Sources);

        MarkdownInlineSpan span = Assert.Single(spans);
        Assert.Equal(
            new MarkdownInlineSpan("📖 Hợp đồng - Tr.3", MarkdownInlineStyles.Bold, Citation: CitationSpan.ForSource(Sources[0])),
            span);
    }
}
