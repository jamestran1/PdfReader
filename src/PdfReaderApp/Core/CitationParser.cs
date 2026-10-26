using System.Text.RegularExpressions;
using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>Tách câu trả lời AI thành chữ thường và trích dẫn <c>[n]</c> (cả <c>[1, 2]</c>, <c>[1][2]</c>) khớp
/// với bảng nguồn. Số không có trong bảng, và chữ dở dang lúc đang stream (<c>[</c>, <c>[1</c>), giữ nguyên là chữ thường.</summary>
public static partial class CitationParser
{
    // Cả nhóm trong một cặp ngoặc vuông: "[1]" hoặc "[1, 2]". "[Trang 3]" không khớp.
    [GeneratedRegex(@"\[(?<numbers>\d+(?:\s*,\s*\d+)*)\]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex CitationGroup();

    public static IReadOnlyList<CitationSpan> Parse(string text, IReadOnlyList<RagSource> sources)
    {
        if (string.IsNullOrEmpty(text)) return [];
        if (sources.Count == 0) return [new CitationSpan(text)];

        var spans = new List<CitationSpan>();
        var plain = new System.Text.StringBuilder();
        int cursor = 0;
        foreach (Match match in CitationGroup().Matches(text))
        {
            plain.Append(text, cursor, match.Index - cursor);
            cursor = match.Index + match.Length;
            AppendGroup(match, sources, spans, plain);
        }
        plain.Append(text, cursor, text.Length - cursor);
        FlushPlain(plain, spans);
        return spans;
    }

    private static void AppendGroup(Match match, IReadOnlyList<RagSource> sources, List<CitationSpan> spans, System.Text.StringBuilder plain)
    {
        string[] numbers = match.Groups["numbers"].Value.Split(',', StringSplitOptions.TrimEntries);
        // Cả nhóm một số không có trong bảng: giữ nguyên chữ gốc, kể cả khoảng trắng.
        if (numbers.Length == 1 && FindSource(numbers[0], sources) is null)
        {
            plain.Append(match.Value);
            return;
        }
        foreach (string number in numbers)
        {
            RagSource? source = FindSource(number, sources);
            if (source is null)
            {
                plain.Append('[').Append(number).Append(']');
                continue;
            }
            FlushPlain(plain, spans);
            spans.Add(CitationSpan.ForSource(source));
        }
    }

    private static RagSource? FindSource(string number, IReadOnlyList<RagSource> sources) =>
        int.TryParse(number, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int n) ? sources.FirstOrDefault(s => s.Number == n) : null;

    private static void FlushPlain(System.Text.StringBuilder plain, List<CitationSpan> spans)
    {
        if (plain.Length == 0) return;
        spans.Add(new CitationSpan(plain.ToString()));
        plain.Clear();
    }
}
