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
    public void Resolve_NoteOfDocumentOutsideWorkspace_KeepsDocumentTitleAndPageButCannotJump()
    {
        var entries = CompositionSources.Resolve(
            CompositionOf("x", "n1"), [NoteOf("n1", "docB", 3, "a")], TitleOf, id => id != "docB");

        var entry = Assert.Single(entries);
        Assert.False(entry.IsFree);
        Assert.False(entry.CanJumpToDocument);
        Assert.Equal("docB", entry.DocumentId);
        Assert.Equal("Tr.4", entry.PageLabel);
        Assert.Equal("Báo cáo, Tr.4", entry.Location);
    }

    [Fact]
    public void Resolve_NumbersSourcesInCompositionOrder_WithDocumentPageAndContent()
    {
        var composition = CompositionOf("x", "n2", "n1");
        var notes = new[] { NoteOf("n1", "docA", 3, "ghi chú một"), NoteOf("n2", "docB", 6, "ghi chú hai", "câu trích") };

        var entries = CompositionSources.Resolve(composition, notes, TitleOf, _ => true);

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
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1", "gone"), [NoteOf("n1", "docA", 0, "a")], TitleOf, _ => true);

        Assert.False(entries[0].IsDeleted);
        Assert.True(entries[1].IsDeleted);
        Assert.Equal(CompositionSources.DeletedNoteText, entries[1].Content);
        Assert.False(entries[1].IsClickable);
    }

    [Fact]
    public void Resolve_NoteEditedAfterGeneration_UsesCurrentContent()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1"), [NoteOf("n1", "docA", 0, "đã sửa")], TitleOf, _ => true);

        Assert.Equal("đã sửa", entries[0].Content);
        Assert.True(entries[0].IsClickable);
    }

    [Fact]
    public void Resolve_FreeNote_HasNoDocumentAndIsFree()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1"), [NoteOf("n1", null, null, "tự do")], TitleOf, _ => true);

        Assert.True(entries[0].IsFree);
        Assert.Null(entries[0].DocumentId);
        Assert.Equal(CompositionSources.FreeNoteLabel, entries[0].Location);
        Assert.True(entries[0].IsClickable);
    }

    private static readonly TimeZoneInfo UtcPlus7 = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(7), "t", "t");
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 8, 0, 0, TimeSpan.Zero); // 15:00 ngày 31/10 theo UTC+7

    private static long Ms(int year, int month, int day, int hour, int minute)
        => new DateTimeOffset(year, month, day, hour, minute, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();

    [Fact]
    public void FormatWhen_SameLocalDay_ShowsHomNayWithTime()
    {
        Assert.Equal("Hôm nay 10:05", CompositionSources.FormatWhen(Ms(2026, 10, 31, 3, 5), Now, UtcPlus7));
    }

    [Fact]
    public void FormatWhen_PreviousLocalDay_ShowsHomQuaWithTime()
    {
        Assert.Equal("Hôm qua 21:05", CompositionSources.FormatWhen(Ms(2026, 10, 30, 14, 5), Now, UtcPlus7));
    }

    [Fact]
    public void FormatWhen_OlderSameYear_ShowsDayMonthAndTime()
    {
        Assert.Equal("28/10 09:40", CompositionSources.FormatWhen(Ms(2026, 10, 28, 2, 40), Now, UtcPlus7));
    }

    [Fact]
    public void FormatWhen_OtherYear_ShowsFullDate()
    {
        Assert.Equal("28/10/2025 09:40", CompositionSources.FormatWhen(Ms(2025, 10, 28, 2, 40), Now, UtcPlus7));
    }

    [Fact]
    public void FormatWhen_LocalDayDiffersFromUtcDay_UsesLocalZone()
    {
        // 18:30 UTC ngày 30/10 là 01:30 ngày 31/10 theo UTC+7: vẫn là "Hôm nay".
        Assert.Equal("Hôm nay 01:30", CompositionSources.FormatWhen(Ms(2026, 10, 30, 18, 30), Now, UtcPlus7));
    }

    [Fact]
    public void FormatMeta_JoinsWhenAndNoteCount()
    {
        Assert.Equal("Hôm nay 14:20 · 3 Note", CompositionSources.FormatMeta("Hôm nay 14:20", 3));
    }

    [Fact]
    public void FormatSourcesMeta_JoinsCountModelAndWhen()
    {
        Assert.Equal("Nguồn: 4 Note · model-x · Hôm qua 21:05", CompositionSources.FormatSourcesMeta(4, "model-x", "Hôm qua 21:05"));
    }

    [Fact]
    public void SourceLine_DocumentNote_ShowsDocumentPageAndContent()
    {
        var entry = new CompositionSourceEntry(1, "n1", "docB", "Báo cáo", 6, null, "ghi chú", false);

        Assert.Equal("Báo cáo, tr. 7: ghi chú", entry.SourceLine);
    }

    [Fact]
    public void SourceLine_FreeNote_ShowsFreeLabelAndContent()
    {
        var entry = new CompositionSourceEntry(2, "n2", null, null, null, null, "ý tưởng", false);

        Assert.Equal("Note tự do: ý tưởng", entry.SourceLine);
    }

    [Fact]
    public void SourceLine_DeletedNote_ShowsOnlyDeletedText()
    {
        var entry = new CompositionSourceEntry(3, "n3", null, null, null, null, CompositionSources.DeletedNoteText, true);

        Assert.Equal(CompositionSources.DeletedNoteText, entry.SourceLine);
    }

    [Fact]
    public void ParseCitations_LiveSource_BecomesClickableNumberLink()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1", "n2"), [NoteOf("n1", "docA", 3, "a"), NoteOf("n2", "docB", 6, "b")], TitleOf, _ => true);

        var spans = CompositionSources.ParseCitations("Ý chính [2] và [1, 2].", entries);

        var links = spans.Where(s => s.Source is not null).ToList();
        Assert.Equal(["[2]", "[1]", "[2]"], [.. links.Select(s => s.Text)]);
        Assert.Equal(["n2", "n1", "n2"], [.. links.Select(s => s.Source!.DocumentId)]);
        Assert.Equal("Ý chính ", spans[0].Text);
    }

    [Fact]
    public void ParseCitations_DeletedSource_StaysPlainText_AndContentUnchanged()
    {
        var entries = CompositionSources.Resolve(CompositionOf("x", "n1", "gone"), [NoteOf("n1", "docA", 0, "a")], TitleOf, _ => true);

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
