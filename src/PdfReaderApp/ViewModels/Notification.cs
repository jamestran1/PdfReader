namespace PdfReaderApp.ViewModels;

/// <summary>Một thông báo snackbar: nội dung + có phải lỗi không (đổi icon/màu).</summary>
public sealed record Notification(string Message, bool IsError);
