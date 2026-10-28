namespace PdfReaderApp.ViewModels;

/// <summary>Phần của Search mà Phiên đọc điều khiển. Phiên đọc quyết định lúc nào xoá; Search chỉ thực thi.</summary>
public interface IReadingSessionSearch
{
    /// <summary>Xoá ô tìm, kết quả và Phạm vi.</summary>
    void Reset();

    /// <summary>Chỉ xoá kết quả, giữ ô tìm.</summary>
    void ClearResults();
}
