using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class CompositionContextBuilderTests
{
    private static readonly CompositionSourceDocument DocA = new("a", "Sách A");
    private static readonly CompositionSourceDocument DocB = new("b", "Sách B");

    private static Note NoteOf(string id, string? docId, int? pageIndex, string? quote, string content, long createdAt = 0)
        => new(id, "ws", docId, pageIndex, quote, content, createdAt, createdAt);

    [Fact]
    public void Build_OrdersByWorkspaceDocumentThenPageThenFreeNotesByCreation_AndNumbersInThatOrder()
    {
        Note[] notes =
        [
            NoteOf("free-late", null, null, null, "tự do muộn", createdAt: 90),
            NoteOf("a5", "a", 4, null, "A trang 5"),
            NoteOf("b1", "b", 0, null, "B trang 1"),
            NoteOf("free-early", null, null, null, "tự do sớm", createdAt: 10),
            NoteOf("a2", "a", 1, null, "A trang 2"),
        ];

        var context = CompositionContextBuilder.Build(notes, [DocA, DocB]);

        Assert.Equal(["a2", "a5", "b1", "free-early", "free-late"], context.SourceNoteIds);
        int p1 = context.Text.IndexOf("[1] ", StringComparison.Ordinal);
        int p2 = context.Text.IndexOf("[2] ", StringComparison.Ordinal);
        int p5 = context.Text.IndexOf("[5]", StringComparison.Ordinal);
        Assert.True(p1 >= 0 && p1 < p2 && p2 < p5);
        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 2", context.Text, StringComparison.Ordinal);
        Assert.Contains("[3] Tài liệu: \"Sách B\", Trang: 1", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_NoteWithoutQuote_SendsOnlyContent()
    {
        var context = CompositionContextBuilder.Build([NoteOf("n", "a", 0, null, "ý của tôi")], [DocA]);

        Assert.Contains("Ghi chú: ý của tôi", context.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Trích", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_HighlightWithEmptyContent_SendsOnlyQuote()
    {
        var context = CompositionContextBuilder.Build([NoteOf("n", "a", 0, "câu được tô", "")], [DocA]);

        Assert.Contains("Trích: \"câu được tô\"", context.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("Ghi chú", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FreeNote_HasNoDocumentNameOrPage()
    {
        var context = CompositionContextBuilder.Build([NoteOf("n", null, null, null, "ý tưởng rời")], [DocA]);

        Assert.Equal("[1]\nGhi chú: ý tưởng rời", context.Text);
    }

    [Fact]
    public void Build_NoteOfDocumentOutsideTheWorkspaceList_IsTreatedAsFreeNote()
    {
        var context = CompositionContextBuilder.Build(
            [NoteOf("gone", "removed", 2, null, "mồ côi"), NoteOf("a", "a", 0, null, "có doc")], [DocA]);

        Assert.Equal(["a", "gone"], context.SourceNoteIds);
        Assert.DoesNotContain("Trang: 3", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_SkipsNoteWithNeitherQuoteNorContent()
    {
        var context = CompositionContextBuilder.Build([NoteOf("empty", "a", 0, null, "  "), NoteOf("n", "a", 1, null, "x")], [DocA]);

        Assert.Equal(["n"], context.SourceNoteIds);
        Assert.StartsWith("[1] ", context.Text, StringComparison.Ordinal);
    }
}
