namespace PdfReaderApp.Services;

/// <summary>Document của Workspace, theo thứ tự hiển thị trong Workspace, dùng để xếp và gắn nhãn Note nguồn.</summary>
public sealed record CompositionSourceDocument(string DocumentId, string Title);
