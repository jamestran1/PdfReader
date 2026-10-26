namespace PdfReaderApp.Models;

/// <summary>Chunk kèm khoảng cách tới vector truy vấn (nhỏ hơn là gần hơn). Khoảng cách chỉ so sánh được
/// giữa các chunk cùng Embedding Model, nên dùng để gộp kết quả của nhiều tài liệu khi truy vấn cùng một model.</summary>
public sealed record ScoredChunk(Chunk Chunk, double Distance);
