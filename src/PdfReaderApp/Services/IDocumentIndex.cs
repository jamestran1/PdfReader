using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IDocumentIndex : IDisposable
{
    bool VectorSearchAvailable { get; }
    void EnsureSchema();
    DocumentIndexStatus GetStatus(string documentId, string embeddingModel);
    void DeleteDocument(string documentId);
    IReadOnlyList<long> WriteChunks(
        string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks);
    IReadOnlyList<StoredChunk> GetChunks(string documentId);
    void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings);
    void DeleteEmbeddings(string documentId, string embeddingModel);
    /// <summary>Giành quyền lập chỉ mục (document, model) một cách nguyên tử: đặt trạng thái <c>Indexing</c> và trả true,
    /// hoặc trả false nếu đã có lượt khác đang lập chỉ mục. Lượt thắng phải kết thúc bằng Complete, Partial, TextOnly hoặc Empty.</summary>
    /// <param name="documentId">Tài liệu cần lập chỉ mục.</param>
    /// <param name="embeddingModel">Embedding Model của Vector Index sẽ ghi.</param>
    /// <param name="claimToken">Định danh của quyền vừa giành được; chỉ có nghĩa khi trả true. Đưa lại cho <see cref="ReleaseIndexing"/>.</param>
    bool TryBeginIndexing(string documentId, string embeddingModel, out long claimToken);

    /// <summary>Nhả quyền của chính mình: đặt <c>Partial</c> chỉ khi quyền còn là <paramref name="claimToken"/>. Nếu tài liệu đã bị
    /// xoá rồi lượt khác giành quyền mới (lập chỉ mục lại), lượt cũ nhả muộn không được đè lên quyền của lượt mới.</summary>
    void ReleaseIndexing(string documentId, string embeddingModel, long claimToken);

    /// <summary>Ghi lô vector chỉ khi <paramref name="claimToken"/> vẫn là claim hiện tại, kiểm và ghi trong cùng một lock.
    /// Trả false (không ghi gì) nếu lượt này đã mất claim, vd tài liệu bị xoá để lập chỉ mục lại: vector cho chunk đã xoá chỉ là mồ côi.</summary>
    bool WriteEmbeddingsIfClaimed(
        string documentId, string embeddingModel, long claimToken, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings);

    /// <summary>Kết thúc lượt lập chỉ mục bằng <paramref name="status"/> (Complete, Empty hoặc TextOnly) và bỏ claim, chỉ khi
    /// <paramref name="claimToken"/> vẫn là claim hiện tại. Trả false nếu lượt này đã bị thay thế: không được đè trạng thái của lượt mới.</summary>
    bool FinishIndexing(string documentId, string embeddingModel, long claimToken, DocumentIndexStatus status);
    void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel);
    IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50);
    IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5);

    /// <summary>Như <see cref="RetrieveRelevant"/> nhưng kèm khoảng cách, tăng dần, để gộp kết quả nhiều tài liệu.</summary>
    IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string documentId, string embeddingModel, float[] queryVector, int k = 5);
}
