using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Ảnh chụp trạng thái tài liệu mà khung chat cần mỗi lần nạp lịch sử hoặc gửi tin.</summary>
/// <param name="DocumentId">Tài liệu đang mở, null khi chưa mở tài liệu nào (không dựng ngữ cảnh RAG).</param>
/// <param name="OwnerId">Nơi lưu lịch sử chat: workspace trong phiên workspace, ngược lại là tài liệu.</param>
/// <param name="Blocks">Khối văn bản của tài liệu, dùng làm ngữ cảnh dự phòng khi RAG không có kết quả.</param>
/// <param name="CurrentPage">Trang đang xem (từ 1), tâm của cửa sổ ngữ cảnh dự phòng.</param>
public sealed record ChatContext(
    string? DocumentId,
    string? OwnerId,
    IReadOnlyList<TextBlock> Blocks,
    int CurrentPage);
