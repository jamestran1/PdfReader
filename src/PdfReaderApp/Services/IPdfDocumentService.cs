using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IPdfDocumentService : IDisposable
{
    void LoadFile(string filePath);
    List<TextBlock> ExtractStructure();
    List<PageText> ExtractPageTexts();

    /// <summary>
    /// Returns bounding boxes (PDF user-space) of every match of <paramref name="query"/> on the
    /// given zero-based page, accent-insensitive and whitespace-tolerant. Empty if no match.
    /// </summary>
    List<MatchRect> FindMatchRects(int pageIndex, string query);
}
