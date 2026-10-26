namespace PdfReaderApp.Models;

/// <summary>Phân loại các Document của Workspace theo trạng thái index của Embedding Model đang chọn.</summary>
/// <param name="Ready">Đã <c>Complete</c>: truy vấn được ngay.</param>
/// <param name="Unindexed">Cần lập chỉ mục nền (chưa có, dở dang, hoặc chỉ có text nhưng nay đã có key).</param>
/// <param name="Indexing">Đang được lập chỉ mục bởi tác vụ khác.</param>
/// <param name="MissingEmbeddingKeyProvider">Provider embedding đang thiếu API key, khi đó không lập chỉ mục và không truy vấn được; null nếu đã có key.</param>
public sealed record WorkspaceIndexPlan(
    IReadOnlyList<WorkspaceDocument> Ready,
    IReadOnlyList<WorkspaceDocument> Unindexed,
    IReadOnlyList<WorkspaceDocument> Indexing,
    AiProvider? MissingEmbeddingKeyProvider = null);
