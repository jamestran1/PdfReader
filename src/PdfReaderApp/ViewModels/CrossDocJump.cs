namespace PdfReaderApp.ViewModels;

/// <summary>Cross-doc jump: mở hoặc kích hoạt tab của Document rồi cuộn tới trang.</summary>
/// <param name="documentId">Mã Document đích.</param>
/// <param name="pageIndex">Chỉ số trang đích, tính từ 0.</param>
public delegate void CrossDocJump(string documentId, int pageIndex);
