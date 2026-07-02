using System;

namespace PdfReaderApp.Services;

/// <summary>
/// Dựng đoạn snippet hiển thị từ text GỐC (giữ dấu) quanh vị trí match,
/// dù index/match chạy trên text đã fold (bỏ dấu).
/// </summary>
public static class SearchSnippetBuilder
{
    public static string Build(string originalText, string query, int contextChars = 40)
    {
        if (string.IsNullOrEmpty(originalText)) return "";

        string foldedQuery = SearchNormalizer.Fold(query);
        var (foldedText, map) = SearchNormalizer.FoldWithMap(originalText);

        int hit = foldedQuery.Length == 0
            ? -1
            : foldedText.IndexOf(foldedQuery, StringComparison.Ordinal);

        if (hit < 0)
        {
            // Không định vị được match: trả phần đầu text gốc.
            if (originalText.Length <= contextChars * 2) return originalText.Trim();
            return originalText.Substring(0, contextChars * 2).Trim() + "...";
        }

        int srcStart = map[hit];
        int srcEnd = map[hit + foldedQuery.Length - 1] + 1; // exclusive
        int from = Math.Max(0, srcStart - contextChars);
        int to = Math.Min(originalText.Length, srcEnd + contextChars);

        string raw = originalText.Substring(from, to - from);
        int leadTrim = raw.Length - raw.TrimStart().Length;
        int trailTrim = raw.Length - raw.TrimEnd().Length;
        string window = raw.Trim();

        // Show "..." only when real (non-whitespace) content was actually cut off.
        bool cutBefore = originalText.Substring(0, from + leadTrim).Trim().Length > 0;
        bool cutAfter = originalText.Substring(to - trailTrim).Trim().Length > 0;
        string prefix = cutBefore ? "..." : "";
        string suffix = cutAfter ? "..." : "";
        return prefix + window + suffix;
    }
}
