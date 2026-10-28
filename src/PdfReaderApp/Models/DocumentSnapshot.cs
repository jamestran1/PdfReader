namespace PdfReaderApp.Models;

/// <summary>Ảnh chụp tài liệu đang mở mà Ngữ cảnh trả lời cần mỗi lần dựng.</summary>
/// <param name="DocumentId">Tài liệu đang mở, null khi chưa mở tài liệu nào (không dựng ngữ cảnh RAG).</param>
/// <param name="Blocks">Khối văn bản của tài liệu, dùng làm ngữ cảnh dự phòng khi RAG không có kết quả.</param>
/// <param name="CurrentPage">Trang đang xem (từ 1), tâm của cửa sổ ngữ cảnh dự phòng.</param>
/// <param name="DocumentTitle">Tên tài liệu đang mở, hiện trong nhãn trích dẫn <c>[n]</c>.</param>
public sealed record DocumentSnapshot(
    string? DocumentId,
    IReadOnlyList<TextBlock> Blocks,
    int CurrentPage,
    string DocumentTitle = "");
