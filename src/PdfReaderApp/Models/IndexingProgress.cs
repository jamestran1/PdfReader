namespace PdfReaderApp.Models;

public sealed record IndexingProgress(int Done, int Total, string Status);
