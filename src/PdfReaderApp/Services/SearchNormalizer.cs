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
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled, TimeSpan.FromSeconds(1));

    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";

        // Step 0: bỏ ký tự mà string.Normalize từ chối (nếu không sẽ ném ArgumentException)
        s = DropInvalidCodePoints(s);

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
        return Whitespace.Replace(s, " ").Trim();
    }

    /// <summary>
    /// Như Fold nhưng trả thêm bản đồ vị trí: map[i] là chỉ số ký tự trong chuỗi gốc
    /// tương ứng ký tự folded thứ i. Dùng để định vị match trên text gốc (giữ dấu).
    /// </summary>
    public static (string folded, int[] map) FoldWithMap(string s)
    {
        if (string.IsNullOrEmpty(s)) return ("", System.Array.Empty<int>());

        // Phase 1: fold theo từng đơn vị mã (cặp surrogate hợp lệ là một đơn vị), ghi nhớ chỉ số nguồn.
        var chars = new List<char>(s.Length);
        var src = new List<int>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            int length = char.IsHighSurrogate(s[i]) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]) ? 2 : 1;
            foreach (char d in FoldUnit(s.Substring(i, length)))
            {
                chars.Add(d);
                src.Add(i);
            }
            i += length - 1;
        }

        // Phase 2: gộp chuỗi whitespace thành 1 space, trim hai đầu (khớp Whitespace.Replace + Trim).
        var sb = new StringBuilder(chars.Count);
        var map = new List<int>(chars.Count);
        bool pendingSpace = false;
        int pendingSrc = -1;
        for (int k = 0; k < chars.Count; k++)
        {
            if (char.IsWhiteSpace(chars[k]))
            {
                if (!pendingSpace) { pendingSpace = true; pendingSrc = src[k]; }
                continue;
            }
            if (pendingSpace && sb.Length > 0) // run whitespace noi bo -> 1 space
            {
                // map collapsed internal whitespace to the FIRST whitespace char of the run
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

    // Fold một đơn vị mã (một char hoặc một cặp surrogate): bỏ ký tự không hợp lệ, đổi đ, bỏ dấu, hạ chữ thường.
    private static IEnumerable<char> FoldUnit(string unit)
    {
        unit = DropInvalidCodePoints(unit);
        if (unit.Length == 0) yield break;

        // Đ/đ không tách được bằng NFD nên đổi trước
        if (unit[0] == 'đ') unit = "d";
        else if (unit[0] == 'Đ') unit = "D";

        foreach (char d in unit.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(d) != UnicodeCategory.NonSpacingMark)
                yield return char.ToLowerInvariant(d);
        }
    }

    /// <summary>
    /// Bỏ surrogate lẻ và noncharacter (U+FFFE, U+FFFF mỗi plane, U+FDD0..U+FDEF): <c>string.Normalize</c> ném
    /// ArgumentException với chúng. PDFium dùng U+FFFE đánh dấu gạch nối mềm trong vùng chọn nên bỏ đi
    /// để "para-phrase" khớp "paraphrase" trên trang.
    /// </summary>
    private static string DropInvalidCodePoints(string s)
    {
        int firstBad = -1;
        for (int i = 0; i < s.Length && firstBad < 0; i++)
        {
            if (IsInvalidAt(s, i, out int length)) firstBad = i;
            else i += length - 1;
        }
        if (firstBad < 0) return s;

        var sb = new StringBuilder(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            bool bad = IsInvalidAt(s, i, out int length);
            if (!bad) sb.Append(s, i, length);
            i += length - 1;
        }
        return sb.ToString();
    }

    private static bool IsInvalidAt(string s, int index, out int length)
    {
        if (char.IsHighSurrogate(s[index]) && index + 1 < s.Length && char.IsLowSurrogate(s[index + 1]))
        {
            length = 2;
            return IsNoncharacter(char.ConvertToUtf32(s[index], s[index + 1]));
        }
        length = 1;
        return char.IsSurrogate(s[index]) || IsNoncharacter(s[index]);
    }

    private static bool IsNoncharacter(int codePoint) =>
        codePoint is >= 0xFDD0 and <= 0xFDEF || (codePoint & 0xFFFE) == 0xFFFE;
}
