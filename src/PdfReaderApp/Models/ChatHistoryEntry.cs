namespace PdfReaderApp.Models;

/// <summary>Một tin nhắn chat đã lưu, gắn với một documentId.</summary>
public sealed record ChatHistoryEntry(string DocumentId, string Role, string Content, long CreatedAtUnix);
