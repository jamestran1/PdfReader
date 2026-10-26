namespace PdfReaderApp.Models;

/// <summary>
/// A search-match bounding box on a single page, in PDF points relative to the bottom-left corner of the
/// page's CropBox (the visible area PDFium renders), not the MediaBox origin.
/// Produced by the text engine (iText) so it tracks the real glyph layout.
/// </summary>
public sealed record MatchRect(float PdfX, float PdfY, float Width, float Height);
