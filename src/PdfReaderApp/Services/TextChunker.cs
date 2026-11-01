using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public static class TextChunker
{
    public static IReadOnlyList<Chunk> Chunk(
        string documentId, IReadOnlyList<TextBlock> blocks, int maxChars = 900, int overlap = 100)
    {
        var result = new List<Chunk>();
        int ordinal = 0;

        foreach (var pageGroup in blocks.GroupBy(b => b.PageIndex).OrderBy(g => g.Key))
        {
            string pageText = string.Join(" ", pageGroup.Select(b => b.Text)).Trim();
            AddPageChunks(result, documentId, pageGroup.Key, pageText, maxChars, overlap, ref ordinal);
        }

        return result;
    }

    public static IReadOnlyList<Chunk> ChunkPages(
        string documentId, IReadOnlyList<PageText> pages, int maxChars = 900, int overlap = 100)
    {
        var result = new List<Chunk>();
        int ordinal = 0;

        foreach (var page in pages.OrderBy(p => p.PageIndex))
            AddPageChunks(result, documentId, page.PageIndex, page.Text.Trim(), maxChars, overlap, ref ordinal);

        return result;
    }

    private static void AddPageChunks(
        List<Chunk> result, string documentId, int pageIndex, string pageText, int maxChars, int overlap, ref int ordinal)
    {
        int start = 0;
        while (start < pageText.Length)
        {
            int hardEnd = Math.Min(start + maxChars, pageText.Length);
            int end = SnapOutOfSurrogatePair(pageText, hardEnd);
            if (end <= start) end = hardEnd; // maxChars quá nhỏ để chứa một cặp: chấp nhận cắt
            result.Add(new Chunk(documentId, pageIndex, ordinal++, pageText[start..end]));

            if (end >= pageText.Length) break;
            start = SnapOutOfSurrogatePair(pageText, start + Math.Max(1, maxChars - overlap));
        }
    }

    /// <summary>Lùi <paramref name="index"/> một ký tự nếu nó rơi vào giữa cặp surrogate. Cắt giữa cặp (emoji, ký tự ngoài BMP)
    /// sinh chuỗi Unicode không hợp lệ, về sau làm <c>string.Normalize</c> ném ArgumentException khi tô nguồn trích dẫn.</summary>
    private static int SnapOutOfSurrogatePair(string text, int index)
        => index > 0 && index < text.Length && char.IsLowSurrogate(text[index]) && char.IsHighSurrogate(text[index - 1])
            ? index - 1
            : index;
}
