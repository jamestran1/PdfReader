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

    public static IReadOnlyList<CompositionSourceEntry> Resolve(
        Composition composition, IReadOnlyList<Note> currentNotes, Func<string, string?> documentTitle)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(currentNotes);
        ArgumentNullException.ThrowIfNull(documentTitle);

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
            entries.Add(new CompositionSourceEntry(i + 1, noteId, note.DocumentId, title, note.PageIndex, note.Quote, note.Content, false));
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

    /// <summary>"Sinh bởi {Chat Model} lúc HH:mm dd/MM/yyyy" theo múi giờ <paramref name="zone"/>.</summary>
    public static string FormatGenerated(string chatModel, long createdAtUnixMs, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var local = TimeZoneInfo.ConvertTime(DateTimeOffset.FromUnixTimeMilliseconds(createdAtUnixMs), zone);
        return $"Sinh bởi {chatModel} lúc {local.ToString("HH:mm dd/MM/yyyy", CultureInfo.InvariantCulture)}";
    }
}
