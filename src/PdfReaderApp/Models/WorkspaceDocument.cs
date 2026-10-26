namespace PdfReaderApp.Models;

/// <summary>Một Document thuộc Workspace (membership) kèm tên và đường dẫn file trong thư viện; dùng cho hỏi đáp
/// Toàn Workspace: truy vấn vector theo <see cref="DocumentId"/>, đánh số nguồn theo <see cref="Title"/>,
/// và lập chỉ mục nền từ <see cref="FilePath"/> khi tài liệu chưa có index.</summary>
public sealed record WorkspaceDocument(string DocumentId, string Title, string FilePath);
