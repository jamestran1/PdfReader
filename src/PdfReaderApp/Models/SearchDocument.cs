namespace PdfReaderApp.Models;

/// <summary>Một Document thuộc Workspace sẽ được quét khi tìm Toàn Workspace.</summary>
public sealed record SearchDocument(string DocumentId, string Title);
