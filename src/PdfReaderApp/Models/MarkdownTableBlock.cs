namespace PdfReaderApp.Models;

/// <summary>Dữ liệu bảng GFM: cột, hàng header, hàng thân và căn cột theo đúng thứ tự Markdown.</summary>
public sealed record MarkdownTableBlock(
    IReadOnlyList<MarkdownTableAlignment> Alignments,
    IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>> Header,
    IReadOnlyList<IReadOnlyList<IReadOnlyList<MarkdownInlineSpan>>> Rows) : MarkdownPresentationBlock;
