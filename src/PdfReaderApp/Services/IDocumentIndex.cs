using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IDocumentIndex : IDisposable
{
    void EnsureSchema();
    DocumentIndexStatus GetStatus(string documentId, string embeddingModel);
    void DeleteDocument(string documentId);
    IReadOnlyList<long> WriteChunks(
        string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks);
    void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings);
    void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel);
    List<SearchResult> SearchText(string documentId, string query, int limit = 50);
    List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5);
}
