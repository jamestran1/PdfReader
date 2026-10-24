namespace PdfReaderApp.Models;

public sealed record SearchResult(int PageIndex, string Snippet, long ChunkId);
