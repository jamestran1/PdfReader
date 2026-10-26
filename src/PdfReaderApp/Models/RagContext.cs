namespace PdfReaderApp.Models;

/// <summary>Ngữ cảnh RAG đã đánh số <c>[n]</c> gửi model, kèm bảng nguồn để biến trích dẫn trong câu trả lời thành link.</summary>
public sealed record RagContext(string Text, IReadOnlyList<RagSource> Sources);
