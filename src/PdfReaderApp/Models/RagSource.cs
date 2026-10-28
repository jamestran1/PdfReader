namespace PdfReaderApp.Models;

/// <summary>Một dòng của bảng nguồn trích dẫn: số tham chiếu <c>[Number]</c> trong ngữ cảnh và câu trả lời của AI
/// ứng với một đoạn của Document. Số tham chiếu thay cho mã Document (hash dài) để AI không phải chép lại mã.</summary>
/// <param name="Number">Số tham chiếu <c>[n]</c>, từ 1.</param>
/// <param name="DocumentId">Document chứa đoạn nguồn.</param>
/// <param name="Title">Tên Document, hiện trong nhãn trích dẫn.</param>
/// <param name="PageIndex">Trang của đoạn nguồn, tính từ 0.</param>
/// <param name="Passage">Chữ của đoạn nguồn đã gửi model, để tô đúng đoạn đó trên trang. Null với nguồn lưu từ bản cũ.</param>
public sealed record RagSource(int Number, string DocumentId, string Title, int PageIndex, string? Passage = null);
