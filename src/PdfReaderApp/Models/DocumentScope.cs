namespace PdfReaderApp.Models;

/// <summary>Phạm vi của một truy vấn (tìm kiếm, hỏi đáp AI): tài liệu đang mở hoặc mọi Document trong Workspace.</summary>
public enum DocumentScope
{
    CurrentDocument,
    Workspace,
}
