using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class ChatMessageMarkdownTests
{
    [Fact]
    public void Content_AIWithMarkdown_ExposesSafeInlinePresentation()
    {
        var message = new ChatMessage { Role = "AI", Content = "**Trả lời** `cú pháp` [Open](https://example.com)" };

        Assert.Equal(
        [
            new MarkdownInlineSpan("Trả lời", MarkdownInlineStyles.Bold),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("cú pháp", MarkdownInlineStyles.Code),
            new MarkdownInlineSpan(" ", MarkdownInlineStyles.None),
            new MarkdownInlineSpan("Open", MarkdownInlineStyles.None, "https://example.com/"),
        ], message.MarkdownSpans);
    }

    [Fact]
    public void Content_UserWithMarkdownSyntax_StaysPlain()
    {
        const string content = "**Không render** [Open](https://example.com)";
        var message = new ChatMessage { Role = "User", Content = content };

        Assert.Empty(message.MarkdownSpans);
        Assert.Equal([new CitationSpan(content)], message.Spans);
    }

    [Fact]
    public void Content_LegacyPlainAI_ProducesOneUnchangedSpan()
    {
        const string content = "Câu trả lời cũ không có Markdown";
        var message = new ChatMessage { Role = "AI", Content = content };

        MarkdownInlineSpan span = Assert.Single(message.MarkdownSpans);
        Assert.Equal(new MarkdownInlineSpan(content), span);
    }
}
