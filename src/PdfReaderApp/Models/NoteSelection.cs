namespace PdfReaderApp.Models;

/// <summary>Kết quả một vùng chọn text trên trang để tạo note: đoạn trích + trang chứa nó (0-based).</summary>
public sealed record NoteSelection(string Quote, int PageIndex);
