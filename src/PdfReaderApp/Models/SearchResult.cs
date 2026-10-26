namespace PdfReaderApp.Models;

/// <summary>Một kết quả tìm kiếm. DocumentId/DocumentTitle chỉ có khi tìm theo phạm vi Toàn Workspace.</summary>
public sealed record SearchResult(
    int PageIndex,
    string Snippet,
    long ChunkId,
    string? DocumentId = null,
    string? DocumentTitle = null);
