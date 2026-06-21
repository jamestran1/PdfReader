using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PdfReaderApp.Services;

/// <summary>
/// Folds text to lowercase ASCII-ish form for accent-insensitive search.
/// Vietnamese d-stroke (d with stroke, U+0111/U+0110) is handled explicitly
/// because NFD decomposition does NOT split it into base + combining mark.
/// </summary>
public static class SearchNormalizer
{
    private static readonly Regex _whitespace = new(@"\s+", RegexOptions.Compiled);

    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // Step 1: replace Vietnamese d-stroke before NFD so it survives decomposition
        s = s.Replace('đ', 'd').Replace('Đ', 'D');

        // Step 2: NFD-normalize so precomposed Vietnamese characters become base + combining marks
        s = s.Normalize(NormalizationForm.FormD);

        // Step 3: drop all combining (non-spacing) marks
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }

        // Step 4: lowercase
        s = sb.ToString().ToLowerInvariant();

        // Step 5: collapse any run of whitespace to a single space and trim
        return _whitespace.Replace(s, " ").Trim();
    }

    /// <summary>
    /// Nhu Fold nhung tra them ban do vi tri: map[i] la chi so ky tu trong chuoi goc
    /// tuong ung ky tu folded thu i. Dung de dinh vi match tren text goc (giu dau).
    /// </summary>
    public static (string folded, int[] map) FoldWithMap(string s)
    {
        if (string.IsNullOrEmpty(s)) return ("", System.Array.Empty<int>());

        // Phase 1: fold theo tung ky tu goc, ghi nho chi so nguon.
        var chars = new List<char>(s.Length);
        var src = new List<int>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c0 = s[i];
            if (c0 == 'đ') c0 = 'd';
            else if (c0 == 'Đ') c0 = 'D';

            string decomposed = c0.ToString().Normalize(NormalizationForm.FormD);
            foreach (char d in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark)
                    continue;
                chars.Add(char.ToLowerInvariant(d));
                src.Add(i);
            }
        }

        // Phase 2: gop run whitespace thanh 1 space, trim hai dau (khop _whitespace.Replace + Trim).
        var sb = new StringBuilder(chars.Count);
        var map = new List<int>(chars.Count);
        bool pendingSpace = false;
        int pendingSrc = 0;
        for (int k = 0; k < chars.Count; k++)
        {
            if (char.IsWhiteSpace(chars[k]))
            {
                if (!pendingSpace) { pendingSpace = true; pendingSrc = src[k]; }
                continue;
            }
            if (pendingSpace && sb.Length > 0) // run whitespace noi bo -> 1 space
            {
                sb.Append(' ');
                map.Add(pendingSrc);
            }
            pendingSpace = false;
            sb.Append(chars[k]);
            map.Add(src[k]);
        }
        // pendingSpace con lai o cuoi bi bo (trim trailing).

        return (sb.ToString(), map.ToArray());
    }
}
