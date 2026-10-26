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
    void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel);
    IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50);
    IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5);

    /// <summary>Như <see cref="RetrieveRelevant"/> nhưng kèm khoảng cách, tăng dần, để gộp kết quả nhiều tài liệu.</summary>
    IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string documentId, string embeddingModel, float[] queryVector, int k = 5);
}
