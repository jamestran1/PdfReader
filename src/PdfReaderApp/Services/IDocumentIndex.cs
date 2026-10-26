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
    bool TryBeginIndexing(string documentId, string embeddingModel);
    void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel);
    IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50);
    IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5);

    /// <summary>Như <see cref="RetrieveRelevant"/> nhưng kèm khoảng cách, tăng dần, để gộp kết quả nhiều tài liệu.</summary>
    IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string documentId, string embeddingModel, float[] queryVector, int k = 5);
}
