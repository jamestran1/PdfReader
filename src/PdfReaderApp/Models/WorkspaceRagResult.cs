namespace PdfReaderApp.Models;

/// <summary>Kết quả dựng ngữ cảnh RAG xuyên Workspace.</summary>
/// <param name="Context">Ngữ cảnh đã đánh số <c>[n]</c>, null khi không có đoạn nào dùng được.</param>
/// <param name="Sources">Bảng nguồn <c>n → (DocumentId, Title, PageIndex)</c>, theo thứ tự số tham chiếu.</param>
/// <param name="UnindexedDocuments">Document chưa có index cho Embedding Model đang chọn (cần lập chỉ mục), chưa được dùng cho câu trả lời này.</param>
/// <param name="IndexingDocuments">Document đang được lập chỉ mục bởi tác vụ khác, chưa được dùng cho câu trả lời này.</param>
/// <param name="MissingEmbeddingKeyProvider">Provider embedding đang thiếu API key (không truy vấn và không lập chỉ mục được), null nếu đã có key.</param>
public sealed record WorkspaceRagResult(
    string? Context,
    IReadOnlyList<RagSource> Sources,
    IReadOnlyList<WorkspaceDocument> UnindexedDocuments,
    IReadOnlyList<WorkspaceDocument> IndexingDocuments,
    AiProvider? MissingEmbeddingKeyProvider = null)
{
    /// <summary>Số Document chưa được đưa vào câu trả lời vì chưa lập chỉ mục xong.</summary>
    public int NotIncludedCount => UnindexedDocuments.Count + IndexingDocuments.Count;
}
