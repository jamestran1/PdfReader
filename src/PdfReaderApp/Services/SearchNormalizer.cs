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
}
