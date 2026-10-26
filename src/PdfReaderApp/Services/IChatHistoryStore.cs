using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu lịch sử chat theo từng documentId trong chats.db.</summary>
public interface IChatHistoryStore
{
    void EnsureSchema();

    /// <summary>Lưu một tin; <paramref name="sources"/> là bảng nguồn <c>[n]</c> đi cùng câu trả lời (null nếu không có).</summary>
    void Append(string documentId, string role, string content, long createdAtUnix, IReadOnlyList<RagSource>? sources = null);
    IReadOnlyList<ChatHistoryEntry> GetAll(string documentId);
    void DeleteForDocument(string documentId);
}
