using System.Text.RegularExpressions;
using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>Tách câu trả lời AI thành chữ thường và trích dẫn <c>[n]</c> (cả <c>[1, 2]</c>, <c>[1][2]</c>) khớp
/// với bảng nguồn, kèm câu nguyên văn nếu có (<c>[n: "câu"]</c>). Số không có trong bảng, và chữ dở dang lúc đang stream
/// (<c>[</c>, <c>[1</c>), giữ nguyên là chữ thường; riêng phần câu trích chưa đóng ngoặc thì bị ẩn.</summary>
public static partial class CitationParser
{
    // Cả nhóm trong một cặp ngoặc vuông: "[1]", "[1, 2]" hoặc "[1: "câu"]" (nháy thẳng hoặc cong, dấu ':' tuỳ chọn).
    // "[Trang 3]" không khớp.
    [GeneratedRegex(@"\[(?<numbers>\d+(?:\s*,\s*\d+)*)(?:\s*:?\s*[""“](?<quote>[^""”]*)[""”])?\s*\]", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex CitationGroup();

    // Đuôi câu trả lời đang stream dở: "[1:", "[1: "the sys". Hiện chữ thô trông như lỗi nên ẩn đến khi đóng ngoặc.
    [GeneratedRegex(@"\[(?<numbers>\d+(?:\s*,\s*\d+)*)\s*(?::\s*(?:[""“][^""”]*)?|[""“][^""”]*)$", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex UnclosedQuoteTail();

    /// <summary>Bỏ câu trích khỏi <paramref name="text"/>, giữ số <c>[n]</c>: lịch sử gửi model không cần câu trích
    /// (tốn ngân sách ký tự) còn số trích dẫn thì giữ nguyên.</summary>
    public static string StripQuotes(string text)
    {
        string closed = CitationGroup().Replace(text, m => $"[{m.Groups["numbers"].Value}]");
        return UnclosedQuoteTail().Replace(closed, "");
    }

    public static IReadOnlyList<CitationSpan> Parse(string text, IReadOnlyList<RagSource> sources)
    {
        if (string.IsNullOrEmpty(text)) return [];
        if (sources.Count == 0) return [new CitationSpan(text)];

        text = HideUnclosedQuote(text, sources);
        if (text.Length == 0) return [];

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

    // Chỉ ẩn khi số đầu là nguồn có thật: "[2020: "..." trong văn thường không phải trích dẫn.
    private static string HideUnclosedQuote(string text, IReadOnlyList<RagSource> sources)
    {
        var tail = UnclosedQuoteTail().Match(text);
        if (!tail.Success) return text;
        string first = tail.Groups["numbers"].Value.Split(',', StringSplitOptions.TrimEntries)[0];
        return FindSource(first, sources) is null ? text : text[..tail.Index];
    }

    private static void AppendGroup(Match match, IReadOnlyList<RagSource> sources, List<CitationSpan> spans, System.Text.StringBuilder plain)
    {
        string[] numbers = match.Groups["numbers"].Value.Split(',', StringSplitOptions.TrimEntries);
        string? quote = match.Groups["quote"].Success && match.Groups["quote"].Value.Trim() is { Length: > 0 } q ? q : null;
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
            // Hai trích dẫn liền nhau ([1][2], [1, 2]) ngăn bằng ", " để các link không dính vào nhau.
            if (plain.Length == 0 && spans.Count > 0 && spans[^1].Source is not null)
                plain.Append(", ");
            FlushPlain(plain, spans);
            // Một câu trích đi với đúng một số; nhiều số chung một câu thì không biết câu thuộc nguồn nào.
            spans.Add(CitationSpan.ForSource(source, numbers.Length == 1 ? quote : null));
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
