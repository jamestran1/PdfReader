namespace PdfReaderApp.Models;

public sealed record Chunk(string DocumentId, int PageIndex, int Ordinal, string Text);
