namespace PdfReaderApp.Models;

/// <summary>Một đoạn chữ hiển thị được sau khi parse Markdown; URL null nghĩa là chữ thường.</summary>
/// <param name="Text">Chữ hiển thị, đã escape khỏi cú pháp Markdown.</param>
/// <param name="Styles">Kiểu chữ áp dụng cho cả đoạn này.</param>
/// <param name="Url">URL ngoài http/https an toàn; null với chữ thường hoặc link bị từ chối.</param>
public sealed record MarkdownInlineSpan(
    string Text,
    MarkdownInlineStyles Styles = MarkdownInlineStyles.None,
    string? Url = null);
