namespace PdfReaderApp.ViewModels;

/// <summary>Phần của Chat mà Phiên đọc điều khiển.</summary>
public interface IReadingSessionChat
{
    /// <summary>Huỷ câu trả lời AI đang chạy.</summary>
    void CancelPendingRequest();

    /// <summary>Nạp lại lịch sử theo Document và Workspace hiện tại của Phiên đọc.</summary>
    void LoadHistory();
}
