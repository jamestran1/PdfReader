using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>
/// Dựng ngữ cảnh Bản soạn từ Note (thuần, không I/O). Thứ tự: Document theo thứ tự trong Workspace, trong Document theo trang,
/// rồi Note của Document đã ra khỏi Workspace nhưng còn trong Library (theo id Document, rồi trang, rồi thời điểm tạo; vẫn ghi tên Document và trang),
/// rồi Note tự do theo thời điểm tạo. Đánh số <c>[n]</c> theo đúng thứ tự này.
/// </summary>
internal static class CompositionContextBuilder
{
    private const int FreeGroup = 2;

    // Nhóm xếp thứ tự: Document trong Workspace, Document ngoài Workspace nhưng còn trong Library, Note tự do.
    private sealed record Placed(Note Note, int Group, int DocumentOrder, string? Title);

    public static CompositionContext Build(
        IReadOnlyList<Note> notes, IReadOnlyList<CompositionSourceDocument> documentsInOrder, CompositionContextOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(documentsInOrder);

        var documentIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < documentsInOrder.Count; i++) documentIndex.TryAdd(documentsInOrder[i].DocumentId, i);

        var ordered = notes.Where(HasText)
            .Select(n => Place(n, documentsInOrder, documentIndex, options?.DocumentTitle))
            .OrderBy(p => p.Group)
            .ThenBy(p => p.DocumentOrder)
            .ThenBy(p => p.Group == FreeGroup ? null : p.Note.DocumentId, StringComparer.Ordinal)
            .ThenBy(p => p.Group == FreeGroup ? -1 : p.Note.PageIndex ?? -1)
            .ThenBy(p => p.Note.CreatedAtUnixMs)
            .ToList();

        var text = new StringBuilder();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0) text.Append("\n\n");
            AppendNote(text, i + 1, ordered[i].Note, ordered[i].Title);
        }

        return new CompositionContext(text.ToString(), [.. ordered.Select(p => p.Note.Id)], options?.AdditionalRequest);
    }

    private static Placed Place(
        Note note, IReadOnlyList<CompositionSourceDocument> documents, Dictionary<string, int> documentIndex, Func<string, string?>? libraryTitle)
    {
        if (note.DocumentId is not { } id) return new Placed(note, FreeGroup, 0, null);
        if (documentIndex.TryGetValue(id, out int index)) return new Placed(note, 0, index, documents[index].Title);
        return libraryTitle?.Invoke(id) is { } title ? new Placed(note, 1, 0, title) : new Placed(note, FreeGroup, 0, null);
    }

    internal static bool HasText(Note note) => !string.IsNullOrWhiteSpace(note.Quote) || !string.IsNullOrWhiteSpace(note.Content);

    private static void AppendNote(StringBuilder text, int number, Note note, string? documentTitle)
    {
        text.Append('[').Append(number).Append(']');
        if (documentTitle is not null)
        {
            text.Append(" Tài liệu: \"").Append(documentTitle).Append('"');
            if (note.PageIndex is int page) text.Append(", Trang: ").Append(page + 1);
        }

        if (!string.IsNullOrWhiteSpace(note.Quote)) text.Append("\nTrích: \"").Append(note.Quote).Append('"');
        if (!string.IsNullOrWhiteSpace(note.Content)) text.Append("\nGhi chú: ").Append(note.Content);
    }
}
