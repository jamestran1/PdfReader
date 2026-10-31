using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Core;

/// <summary>Danh sách nguồn và trích dẫn [n] của Bản soạn (#295): Bản soạn không đổi, nguồn lấy theo Note hiện tại.</summary>
public class CompositionSourcesTests
{
    private static Note NoteOf(string id, string? docId, int? page, string content, string? quote = null)
        => new(id, "ws", docId, page, quote, content, 1, 1);

    private static Composition CompositionOf(string content, params string[] noteIds)
        => new("c1", "ws", "Tóm tắt", CompositionKind.Summary, content, "model-x", noteIds, 0);

    private static string? TitleOf(string documentId) => documentId == "docB" ? "Báo cáo" : null;

    [Fact]
    public void Resolve_NumbersSourcesInCompositionOrder_WithDocumentPageAndContent()
    {
        var composition = CompositionOf("x", "n2", "n1");
        var notes = new[] { NoteOf("n1", "docA", 3, "ghi chú một"), NoteOf("n2", "docB", 6, "ghi chú hai", "câu trích") };

        var entries = CompositionSources.Resolve(composition, notes, TitleOf);

        Assert.Equal(2, entries.Count);
        Assert.Equal(1, entries[0].Number);
        Assert.Equal("n2", entries[0].NoteId);
        Assert.Equal("Báo cáo", entries[0].DocumentTitle);
        Assert.Equal(6, entries[0].PageIndex);
        Assert.Equal("Tr.7", entries[0].PageLabel);
        Assert.Equal("ghi chú hai", entries[0].Content);
        Assert.Equal("câu trích", entries[0].Quote);
        Assert.False(entries[0].IsDeleted);
        Assert.Equal(2, entries[1].Number);
    }

    [Fact]
    public void Resolve_NoteNoLongerExists_IsMarkedDeleted()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1", "gone"), [NoteOf("n1", "docA", 0, "a")], TitleOf);

        Assert.False(entries[0].IsDeleted);
        Assert.True(entries[1].IsDeleted);
        Assert.Equal(CompositionSources.DeletedNoteText, entries[1].Content);
        Assert.False(entries[1].IsClickable);
    }

    [Fact]
    public void Resolve_NoteEditedAfterGeneration_UsesCurrentContent()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1"), [NoteOf("n1", "docA", 0, "đã sửa")], TitleOf);

        Assert.Equal("đã sửa", entries[0].Content);
        Assert.True(entries[0].IsClickable);
    }

    [Fact]
    public void Resolve_FreeNote_HasNoDocumentAndIsFree()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1"), [NoteOf("n1", null, null, "tự do")], TitleOf);

        Assert.True(entries[0].IsFree);
        Assert.Null(entries[0].DocumentId);
        Assert.Equal(CompositionSources.FreeNoteLabel, entries[0].Location);
        Assert.True(entries[0].IsClickable);
    }

    [Fact]
    public void FormatGenerated_ShowsChatModelAndLocalTime()
    {
        var utcPlus7 = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(7), "t", "t");
        long ms = new DateTimeOffset(2026, 10, 31, 3, 5, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

        string text = CompositionSources.FormatGenerated("model-x", ms, utcPlus7);

        Assert.Equal("Sinh bởi model-x lúc 10:05 31/10/2026", text);
    }

    [Fact]
    public void ParseCitations_LiveSource_BecomesClickableNumberLink()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1", "n2"), [NoteOf("n1", "docA", 3, "a"), NoteOf("n2", "docB", 6, "b")], TitleOf);

        var spans = CompositionSources.ParseCitations("Ý chính [2] và [1, 2].", entries);

        var links = spans.Where(s => s.Source is not null).ToList();
        Assert.Equal(["[2]", "[1]", "[2]"], [.. links.Select(s => s.Text)]);
        Assert.Equal(["n2", "n1", "n2"], [.. links.Select(s => s.Source!.DocumentId)]);
        Assert.Equal("Ý chính ", spans[0].Text);
    }

    [Fact]
    public void ParseCitations_DeletedSource_StaysPlainText_AndContentUnchanged()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1", "gone"), [NoteOf("n1", "docA", 0, "a")], TitleOf);

        var spans = CompositionSources.ParseCitations("Có [1] và [2].", entries);

        Assert.Equal(["[1]"], [.. spans.Where(s => s.Source is not null).Select(s => s.Text)]);
        Assert.Equal("Có [1] và [2].", string.Concat(spans.Select(s => s.Text)));
    }

    [Fact]
    public void ParseCitations_NoEntries_ReturnsTextAsOnePlainSpan()
    {
        var spans = CompositionSources.ParseCitations("Có [1].", []);

        var span = Assert.Single(spans);
        Assert.Equal("Có [1].", span.Text);
        Assert.Null(span.Source);
    }
}
