namespace PdfReaderApp.ViewModels;

/// <summary>Phần của Indexing mà Phiên đọc điều khiển.</summary>
public interface IReadingSessionIndexing
{
    /// <summary>Khởi động lập chỉ mục nền cho Document đang đọc.</summary>
    void StartBackgroundIndexing();
}
