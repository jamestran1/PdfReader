using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public static class DocumentContextBuilder
{
    public static string BuildAround(
        IReadOnlyList<TextBlock> blocks, int currentPageOneBased, int window, int maxChars = 48000)
    {
        if (maxChars <= 0) return string.Empty;

        int currentIndex = currentPageOneBased - 1;
        int low = currentIndex - window;
        int high = currentIndex + window;

        var sb = new StringBuilder();
        foreach (var b in blocks)
        {
            if (b.PageIndex < low || b.PageIndex > high) continue;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(b.Text);
            if (sb.Length >= maxChars) break;
        }

        string result = sb.ToString();
        if (result.Length <= maxChars) return result;
        int cut = maxChars;
        if (char.IsHighSurrogate(result[cut - 1])) cut--; // don't split a surrogate pair
        return result[..cut];
    }
}
