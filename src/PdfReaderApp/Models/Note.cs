namespace PdfReaderApp.Models;

/// <summary>Một ghi chú. OwnerKey = phạm vi (v1 = documentId), DocumentId = anchor.
/// Quote = đoạn trích dẫn bôi đen từ trang (nullable; null với note tự do).
/// Rects/Color = highlight trên trang (null nếu note không từ chọn-text).</summary>
public sealed record Note(
    string Id,
    string OwnerKey,
    string? DocumentId,
    int? PageIndex,
    string? Quote,
    string Content,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs,
    IReadOnlyList<HighlightRect>? Rects = null,
    string? Color = null)
{
    /// <summary>Presentation dùng chung với Chat AI; Quote vẫn là dữ liệu nguyên văn của PDF.</summary>
    public IReadOnlyList<MarkdownPresentationBlock> MarkdownBlocks => MarkdownPresentation.ParseBlocks(Content);

    public IReadOnlyList<MarkdownInlineSpan> MarkdownSpans => MarkdownPresentation.ParseInline(Content);
}
