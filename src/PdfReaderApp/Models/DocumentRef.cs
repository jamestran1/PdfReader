namespace PdfReaderApp.Models;

/// <summary>Tham chiếu một Document để dựng Tab: id, tiêu đề và đường dẫn file gói chung (ba chuỗi cùng kiểu dễ đổi chỗ, CS-9).</summary>
public sealed record DocumentRef(string DocumentId, string Title, string Path);
