using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Models;

public sealed class MarkdownPresentationTests
{
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
}
