namespace PdfReaderApp.Services;

/// <summary>Mục đích của văn bản được nhúng. Một số provider (Gemini) cho vector tốt hơn khi biết điều này.</summary>
public enum EmbeddingPurpose
{
    /// <summary>Chunk của tài liệu khi lập chỉ mục.</summary>
    Document,

    /// <summary>Câu hỏi của người dùng khi truy hồi.</summary>
    Query,
}
