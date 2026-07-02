using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu lịch sử chat theo từng documentId trong chats.db.</summary>
public interface IChatHistoryStore
{
    void EnsureSchema();
    void Append(string documentId, string role, string content, long createdAtUnix);
    IReadOnlyList<ChatHistoryEntry> GetAll(string documentId);
    void DeleteForDocument(string documentId);
}
