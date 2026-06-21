namespace PdfReaderApp.Models;

public sealed record PageText(int PageIndex, string Text);

public sealed record Chunk(string DocumentId, int PageIndex, int Ordinal, string Text);

public sealed record SearchResult(int PageIndex, string Snippet, long ChunkId);

public sealed record IndexingProgress(int Done, int Total, string Status);

public enum DocumentIndexStatus
{
    None,
    Indexing,
    Complete,
    Partial,
    TextOnly,
    Empty
}
