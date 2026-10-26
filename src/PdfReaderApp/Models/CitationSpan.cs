namespace PdfReaderApp.Models;

/// <summary>Một đoạn của câu trả lời AI sau khi tách trích dẫn: chữ thường (<see cref="Source"/> null) hoặc
/// một trích dẫn <c>[n]</c> đã khớp bảng nguồn, khi đó <see cref="Text"/> là nhãn hiển thị.</summary>
public sealed record CitationSpan(string Text, RagSource? Source = null)
{
    /// <summary>Nhãn của trích dẫn trong câu, vd "📖 Hợp đồng - Tr.3" (số trang tính từ 1).</summary>
    public static CitationSpan ForSource(RagSource source) =>
        new($"📖 {source.Title} - Tr.{source.PageIndex + 1}", source);
}
