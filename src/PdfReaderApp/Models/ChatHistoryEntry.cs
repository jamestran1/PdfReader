namespace PdfReaderApp.Models;

/// <summary>Một tin nhắn chat đã lưu, gắn với một documentId. <see cref="Sources"/> là bảng nguồn <c>[n]</c>
/// của câu trả lời Toàn Workspace (rỗng với tin khác và tin lưu trước #108).</summary>
public sealed record ChatHistoryEntry(string DocumentId, string Role, string Content, long CreatedAtUnix,
    IReadOnlyList<RagSource> Sources)
{
    public ChatHistoryEntry(string documentId, string role, string content, long createdAtUnix)
        : this(documentId, role, content, createdAtUnix, []) { }
}
