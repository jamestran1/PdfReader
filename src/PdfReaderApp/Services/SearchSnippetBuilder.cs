using System;

namespace PdfReaderApp.Services;

/// <summary>
/// Dung doan snippet hien thi tu text GOC (giu dau) quanh vi tri match,
/// du index/match chay tren text da fold (bo dau).
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
            // Khong dinh vi duoc match: tra phan dau text goc.
            if (originalText.Length <= contextChars * 2) return originalText.Trim();
            return originalText.Substring(0, contextChars * 2).Trim() + "...";
        }

        int srcStart = map[hit];
        int srcEnd = map[hit + foldedQuery.Length - 1] + 1; // exclusive
        int from = Math.Max(0, srcStart - contextChars);
        int to = Math.Min(originalText.Length, srcEnd + contextChars);

        string window = originalText.Substring(from, to - from).Trim();
        string prefix = from > 0 ? "..." : "";
        string suffix = to < originalText.Length ? "..." : "";
        return prefix + window + suffix;
    }
}
