using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>
/// Dựng ngữ cảnh Bản soạn từ Note (thuần, không I/O). Thứ tự: Document theo thứ tự trong Workspace, trong Document theo trang,
/// rồi Note tự do theo thời điểm tạo. Đánh số <c>[n]</c> theo đúng thứ tự này.
/// </summary>
internal static class CompositionContextBuilder
{
    public static CompositionContext Build(IReadOnlyList<Note> notes, IReadOnlyList<CompositionSourceDocument> documentsInOrder)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(documentsInOrder);

        var documentIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < documentsInOrder.Count; i++) documentIndex.TryAdd(documentsInOrder[i].DocumentId, i);

        var usable = notes.Where(HasText).ToList();
        var anchored = usable
            .Where(n => n.DocumentId is not null && documentIndex.ContainsKey(n.DocumentId))
            .OrderBy(n => documentIndex[n.DocumentId!])
            .ThenBy(n => n.PageIndex ?? -1)
            .ThenBy(n => n.CreatedAtUnixMs);
        var free = usable
            .Where(n => n.DocumentId is null || !documentIndex.ContainsKey(n.DocumentId))
            .OrderBy(n => n.CreatedAtUnixMs);
        var ordered = anchored.Concat(free).ToList();

        var text = new StringBuilder();
        for (int i = 0; i < ordered.Count; i++)
        {
            if (i > 0) text.Append("\n\n");
            AppendNote(text, i + 1, ordered[i], documentsInOrder, documentIndex);
        }

        return new CompositionContext(text.ToString(), [.. ordered.Select(n => n.Id)]);
    }

    private static bool HasText(Note note) => !string.IsNullOrWhiteSpace(note.Quote) || !string.IsNullOrWhiteSpace(note.Content);

    private static void AppendNote(
        StringBuilder text, int number, Note note,
        IReadOnlyList<CompositionSourceDocument> documents, Dictionary<string, int> documentIndex)
    {
        text.Append('[').Append(number).Append(']');
        if (note.DocumentId is not null && documentIndex.TryGetValue(note.DocumentId, out int index))
        {
            text.Append(" Tài liệu: \"").Append(documents[index].Title).Append('"');
            if (note.PageIndex is int page) text.Append(", Trang: ").Append(page + 1);
        }

        if (!string.IsNullOrWhiteSpace(note.Quote)) text.Append("\nTrích: \"").Append(note.Quote).Append('"');
        if (!string.IsNullOrWhiteSpace(note.Content)) text.Append("\nGhi chú: ").Append(note.Content);
    }
}
