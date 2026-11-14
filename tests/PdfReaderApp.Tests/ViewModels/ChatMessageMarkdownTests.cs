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
    public void Content_AIWithBlocks_ExposesBlockPresentation()
    {
        var message = new ChatMessage { Role = "AI", Content = "## Tiêu đề\n\n> Trích dẫn" };

        Assert.Equal(
        [
            new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Heading, [new MarkdownInlineSpan("Tiêu đề")], Level: 2),
            new MarkdownPresentationBlock(MarkdownPresentationBlockKind.Quote, [new MarkdownInlineSpan("Trích dẫn")]),
        ], message.MarkdownBlocks);
    }

    [Fact]
    public void Content_AIStreamingChunk_RaisesMarkdownBlocksChanged()
    {
        var message = new ChatMessage { Role = "AI", Content = "# Tiêu" };
        var events = new List<string?>();
        message.PropertyChanged += (_, eventArgs) => events.Add(eventArgs.PropertyName);

        message.Content += " đề";

        Assert.Contains(nameof(ChatMessage.MarkdownBlocks), events, StringComparer.Ordinal);
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
