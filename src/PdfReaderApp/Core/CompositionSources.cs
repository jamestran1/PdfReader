using System.Globalization;
using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>
/// Dựng danh sách nguồn và tách trích dẫn <c>[n]</c> cho Tab Bản soạn. Thuần, không phụ thuộc WPF.
/// Bản soạn không bị sửa: chỉ phần nguồn đọc lại Note hiện tại.
/// </summary>
public static class CompositionSources
{
    public const string DeletedNoteText = "Note đã bị xoá";
    public const string FreeNoteLabel = "Note tự do";
    public const string UnknownDocumentLabel = "Tài liệu không còn trong thư viện";

    /// <summary>Dựng nguồn từ Note hiện tại. <paramref name="isDocumentInWorkspace"/> cho biết Document có thuộc Workspace của
    /// Bản soạn không: Note trỏ tới Document ngoài Workspace vẫn hiện tên Document
    /// và trang, nhưng không Cross-doc jump (khớp <c>CompositionContextBuilder</c>, vốn coi Note đó là Note tự do).</summary>
    public static IReadOnlyList<CompositionSourceEntry> Resolve(
        Composition composition, IReadOnlyList<Note> currentNotes, Func<string, string?> documentTitle,
        Func<string, bool> isDocumentInWorkspace)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(currentNotes);
        ArgumentNullException.ThrowIfNull(documentTitle);
        ArgumentNullException.ThrowIfNull(isDocumentInWorkspace);

        var byId = currentNotes.GroupBy(n => n.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var entries = new List<CompositionSourceEntry>(composition.SourceNoteIds.Count);
        for (int i = 0; i < composition.SourceNoteIds.Count; i++)
        {
            string noteId = composition.SourceNoteIds[i];
            if (!byId.TryGetValue(noteId, out var note))
            {
                entries.Add(new CompositionSourceEntry(i + 1, noteId, null, null, null, null, DeletedNoteText, true));
                continue;
            }

            string? title = note.DocumentId is { } documentId ? documentTitle(documentId) : null;
            bool outside = note.DocumentId is { } id && !isDocumentInWorkspace(id);
            entries.Add(new CompositionSourceEntry(i + 1, noteId, note.DocumentId, title, note.PageIndex, note.Quote, note.Content, false, outside));
        }

        return entries;
    }

    /// <summary>Tách <paramref name="text"/> thành chữ thường và link <c>[n]</c> cho nguồn còn bấm được. Dùng chung
    /// <see cref="CitationParser"/> với Chat: nguồn đã xoá không có trong bảng nên <c>[n]</c> của nó giữ là chữ thường.
    /// <c>RagSource.DocumentId</c> mang id Note (Note tự do không có Document) để lệnh bấm tìm lại nguồn.</summary>
    public static IReadOnlyList<CitationSpan> ParseCitations(string text, IReadOnlyList<CompositionSourceEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var sources = entries
            .Where(e => e.IsClickable)
            .Select(e => new RagSource(e.Number, e.NoteId, e.DocumentTitle ?? FreeNoteLabel, e.PageIndex ?? 0, e.Quote))
            .ToList();
        return [.. CitationParser.Parse(text, sources)
            .Select(s => s.Source is { } source ? new CitationSpan($"[{source.Number}]", source, s.Quote) : s)];
    }

    /// <summary>Thời điểm sinh theo múi giờ <paramref name="zone"/>: "Hôm nay 14:20", "Hôm qua 21:05", "28/10 09:40", hoặc
    /// "28/10/2025 09:40" khi khác năm. <paramref name="now"/> truyền vào để test không phụ thuộc đồng hồ.</summary>
    public static string FormatWhen(long createdAtUnixMs, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(createdAtUnixMs), zone);
        var today = TimeZoneInfo.ConvertTime(now, zone).Date;
        string time = local.ToString("HH:mm", CultureInfo.InvariantCulture);
        if (local.Date == today) return $"Hôm nay {time}";
        if (local.Date == today.AddDays(-1)) return $"Hôm qua {time}";
        string format = local.Year == today.Year ? "dd/MM HH:mm" : "dd/MM/yyyy HH:mm";
        return local.ToString(format, CultureInfo.InvariantCulture);
    }

    /// <summary>Dòng phụ dưới tiêu đề: "{thời điểm} · {n} Note".</summary>
    public static string FormatMeta(string when, int noteCount) => $"{when} · {noteCount} Note";

    /// <summary>Dòng mô tả đầu phần nguồn: "Nguồn: {n} Note · {Chat Model} · {thời điểm}".</summary>
    public static string FormatSourcesMeta(int noteCount, string chatModel, string when)
        => $"Nguồn: {noteCount} Note · {chatModel} · {when}";
}
