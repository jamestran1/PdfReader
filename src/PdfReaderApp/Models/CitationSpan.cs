namespace PdfReaderApp.Models;

/// <summary>Một đoạn của câu trả lời AI sau khi tách trích dẫn: chữ thường (<see cref="Source"/> null) hoặc
/// một trích dẫn <c>[n]</c> đã khớp bảng nguồn, khi đó <see cref="Text"/> là nhãn hiển thị.</summary>
/// <param name="Text">Chữ thường, hoặc nhãn của trích dẫn.</param>
/// <param name="Source">Nguồn được trích; null với chữ thường.</param>
/// <param name="Quote">Câu nguyên văn AI dựa vào (<c>[n: "câu"]</c>); null khi trích dẫn không kèm câu. Thuộc từng lần trích,
/// không thuộc <see cref="RagSource"/>: cùng một nguồn có thể được trích nhiều lần với các câu khác nhau.</param>
public sealed record CitationSpan(string Text, RagSource? Source = null, string? Quote = null)
{
    /// <summary>Nhãn của trích dẫn trong câu, vd "📖 Hợp đồng - Tr.3" (số trang tính từ 1).</summary>
    public static CitationSpan ForSource(RagSource source, string? quote = null) =>
        new($"📖 {source.Title} - Tr.{source.PageIndex + 1}", source, quote);

    /// <summary>Chú giải khi rê chuột lên link: "Tên tài liệu, trang X", thêm câu trích ở dòng dưới khi có; null với chữ thường.</summary>
    public string? Tooltip => Source is null
        ? null
        : $"{Source.Title}, trang {Source.PageIndex + 1}" + (Quote is null ? "" : $"\n“{Quote}”");
}
