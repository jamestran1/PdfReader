namespace PdfReaderApp.ViewModels;

/// <summary>Nguồn gốc của một Cross-doc jump; Phiên đọc quyết định Search và Đoạn nguồn theo giá trị này.</summary>
public enum JumpOrigin
{
    /// <summary>Bấm một Note: Search bị reset.</summary>
    Note,

    /// <summary>Bấm kết quả tìm: ô tìm, kết quả và Phạm vi được giữ để bấm tiếp.</summary>
    Search,

    /// <summary>Bấm trích dẫn AI: Search bị reset, Đoạn nguồn được đặt sau khi nạp thành công.</summary>
    Citation,
}
