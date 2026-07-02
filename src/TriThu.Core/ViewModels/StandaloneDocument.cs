namespace PdfReaderApp.ViewModels;

/// <summary>Tài liệu đang đọc lẻ (Default Workspace) + view-state, để promote sang named Workspace.</summary>
public sealed record StandaloneDocument(string DocumentId, string Title, string Path, int Page, double Zoom);
