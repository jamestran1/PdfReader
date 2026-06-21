namespace PdfReaderApp.Models;

/// <summary>
/// A search-match bounding box on a single page, in PDF user-space (bottom-left origin).
/// Produced by the text engine (iText) so it tracks the real glyph layout.
/// </summary>
public sealed record MatchRect(float PdfX, float PdfY, float Width, float Height);
