namespace PdfReaderApp.Models;

/// <summary>Danh sách nhiệm vụ GFM, giữ trạng thái checkbox và nội dung từng mục.</summary>
public sealed record MarkdownTaskListBlock(IReadOnlyList<MarkdownTaskListItem> Items) : MarkdownPresentationBlock;
