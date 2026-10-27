using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IPdfDocumentService : IDisposable
{
    void LoadFile(string filePath);
    IReadOnlyList<TextBlock> ExtractStructure();
    IReadOnlyList<PageText> ExtractPageTexts();

    /// <summary>
    /// Returns bounding boxes (PDF user-space) of every match of <paramref name="query"/> on the
    /// given zero-based page, accent-insensitive and whitespace-tolerant. Empty if no match.
    /// </summary>
    IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query);

    /// <summary>
    /// Returns bounding boxes (PDF user-space, one per text line) of every phrase in <paramref name="fragments"/>
    /// found on the given zero-based page, matched like <see cref="FindMatchRects"/>. Phrases not on the page are
    /// skipped. Used to highlight the source passage of an AI citation.
    /// </summary>
    IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments);

    /// <summary>
    /// Như <see cref="FindPassageRects"/> nhưng còn báo từng cụm có mặt trên trang hay không, trong một lần xử lý trang.
    /// Dùng để biết câu trích khớp được bao nhiêu.
    /// </summary>
    PassageHits FindPassageHits(int pageIndex, IReadOnlyList<string> fragments);
}
