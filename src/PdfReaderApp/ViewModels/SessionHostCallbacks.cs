namespace PdfReaderApp.ViewModels;

/// <summary>Các hành vi của MainViewModel mà Phiên đọc cần (cần Library và surface nằm ở MainViewModel).</summary>
/// <param name="RefreshNotesDocumentContext">Cập nhật ngữ cảnh chip tài liệu của Notes.</param>
/// <param name="NotifyError">Báo lỗi cho người dùng.</param>
/// <param name="MarkDocumentOpened">Ghi thời điểm mở của Document vào Library.</param>
/// <param name="FindDocument">Tìm Document trong Library của MainViewModel theo id.</param>
internal sealed record SessionHostCallbacks(
    Action RefreshNotesDocumentContext,
    Action<string> NotifyError,
    Action<string> MarkDocumentOpened,
    LibraryDocumentLookup FindDocument);
