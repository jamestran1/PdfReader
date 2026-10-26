namespace PdfReaderApp.Models;

/// <summary>Một dòng của bảng nguồn trích dẫn: số tham chiếu <c>[Number]</c> trong ngữ cảnh và câu trả lời của AI
/// ứng với đoạn ở <see cref="PageIndex"/> (từ 0) của Document <see cref="DocumentId"/>. Số tham chiếu thay cho
/// mã Document (hash dài) để AI không phải chép lại mã.</summary>
public sealed record RagSource(int Number, string DocumentId, string Title, int PageIndex);
