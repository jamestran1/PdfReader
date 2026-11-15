using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

public sealed class SharedMarkdownSecurityPolicyTests
{
    [Fact]
    public void ChatNotesAndComposition_UseTheSameSafeMarkdownPresentation()
    {
        const string markdown = "# Tiêu đề\n\n[Unsafe](javascript:alert(1))\n\n<script>alert(1)</script>";
        var chat = new ChatMessage { Role = "AI", Content = markdown };
        var note = new Note("n1", "ws", null, null, null, markdown, 1, 1);
        var composition = new CompositionTab(new Composition(
            "c1", "ws", "Tóm tắt", CompositionKind.Summary, markdown, "model", [], 1));

        Assert.Equal(chat.MarkdownBlocks, note.MarkdownBlocks);
        Assert.Equal(chat.MarkdownBlocks, composition.MarkdownBlocks);
        Assert.DoesNotContain(
            chat.MarkdownBlocks.SelectMany(MarkdownTestContent.InlineSpans),
            span => span.Url is not null || span.Text.Contains("<script>", StringComparison.Ordinal));
    }
}
