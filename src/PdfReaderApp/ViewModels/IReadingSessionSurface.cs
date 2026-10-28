namespace PdfReaderApp.ViewModels;

/// <summary>Phần của Workspace Documents surface mà Phiên đọc điều khiển.</summary>
public interface IReadingSessionSurface
{
    /// <summary>Làm mới danh sách Document của Workspace đang chọn.</summary>
    void Refresh();
}
