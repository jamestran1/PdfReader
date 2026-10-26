using System.Text;
using System.Text.RegularExpressions;

namespace PdfReaderApp.Services;

/// <summary>
/// Builds a regex pattern for accent-insensitive, whitespace-tolerant phrase matching on PDF
/// text. Each Latin base letter expands to a class of its Vietnamese diacritic variants; letters
/// within a word are separated by "\s*" (tolerates per-glyph extraction spacing) and query spaces
/// become "\s+". Fed to iText's RegexBasedLocationExtractionStrategy to locate match rectangles.
/// </summary>
public static class AccentInsensitiveRegex
{
    private static readonly Dictionary<char, string> Variants = new()
    {
        ['a'] = "aàáảãạăằắẳẵặâầấẩẫậ",
        ['e'] = "eèéẻẽẹêềếểễệ",
        ['i'] = "iìíỉĩị",
        ['o'] = "oòóỏõọôồốổỗộơờớởỡợ",
        ['u'] = "uùúủũụưừứửữự",
        ['y'] = "yỳýỷỹỵ",
        ['d'] = "dđ",
    };

    /// <summary>
    /// Returns a regex pattern (case-insensitive via inline (?i)) matching the folded query
    /// allowing diacritics and arbitrary intra-word whitespace, or "" for an empty query.
    /// </summary>
    public static string BuildPattern(string query)
    {
        string body = BuildBody(query);
        return body.Length == 0 ? "" : "(?i)" + body;
    }

    /// <summary>
    /// One pattern matching any of <paramref name="phrases"/> (each as in <see cref="BuildPattern"/>), so a single
    /// iText pass over the page locates them all. Blank phrases are skipped; "" when none is left.
    /// </summary>
    public static string BuildAlternation(IEnumerable<string> phrases)
    {
        var bodies = phrases.Select(BuildBody).Where(b => b.Length > 0).ToList();
        return bodies.Count switch
        {
            0 => "",
            1 => "(?i)" + bodies[0],
            _ => "(?i)" + string.Join('|', bodies.Select(b => $"(?:{b})")),
        };
    }

    private static string BuildBody(string query)
    {
        string folded = SearchNormalizer.Fold(query);
        if (folded.Length == 0) return "";

        var sb = new StringBuilder();
        bool prevLetter = false;
        foreach (char c in folded)
        {
            if (c == ' ')
            {
                sb.Append("\\s+");
                prevLetter = false;
                continue;
            }
            if (prevLetter) sb.Append("\\s*");
            sb.Append(ClassFor(c));
            prevLetter = true;
        }
        return sb.ToString();
    }

    private static string ClassFor(char c)
        => Variants.TryGetValue(c, out var v) ? "[" + v + "]" : Regex.Escape(c.ToString());
}
