using System.Linq;
using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public static class TextChunker
{
    public static List<Chunk> Chunk(
        string documentId, IReadOnlyList<TextBlock> blocks, int maxChars = 900, int overlap = 100)
    {
        var result = new List<Chunk>();
        int ordinal = 0;

        foreach (var pageGroup in blocks.GroupBy(b => b.PageIndex).OrderBy(g => g.Key))
        {
            int pageIndex = pageGroup.Key;
            string pageText = string.Join(" ", pageGroup.Select(b => b.Text)).Trim();
            if (pageText.Length == 0) continue;

            int start = 0;
            while (start < pageText.Length)
            {
                int len = Math.Min(maxChars, pageText.Length - start);
                string slice = pageText.Substring(start, len);
                result.Add(new Chunk(documentId, pageIndex, ordinal++, slice));

                if (start + len >= pageText.Length) break;
                start += Math.Max(1, maxChars - overlap);
            }
        }

        return result;
    }

    public static List<Chunk> ChunkPages(
        string documentId, IReadOnlyList<PageText> pages, int maxChars = 900, int overlap = 100)
    {
        var result = new List<Chunk>();
        int ordinal = 0;

        foreach (var page in pages.OrderBy(p => p.PageIndex))
        {
            string pageText = page.Text.Trim();
            if (pageText.Length == 0) continue;

            int start = 0;
            while (start < pageText.Length)
            {
                int len = Math.Min(maxChars, pageText.Length - start);
                string slice = pageText.Substring(start, len);
                result.Add(new Chunk(documentId, page.PageIndex, ordinal++, slice));

                if (start + len >= pageText.Length) break;
                start += Math.Max(1, maxChars - overlap);
            }
        }

        return result;
    }
}
