using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public static class PageContextBuilder
{
    /// <summary>Ngữ cảnh theo trang nhưng đánh số <c>[n]</c> như RAG, để câu trả lời lúc RAG không dùng được vẫn bấm được.
    /// Dùng chung bộ định dạng với RAG nên bấm <c>[n]</c> hành xử giống hệt. Null khi cửa sổ trang không có chữ.</summary>
    public static RagContext? BuildNumberedAround(
        WorkspaceDocument document, IReadOnlyList<TextBlock> blocks, int currentPageOneBased, int window,
        int maxChars = ContextLimits.MaxChars)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(blocks);

        int currentIndex = currentPageOneBased - 1;
        var inWindow = blocks
            .Where(b => b.PageIndex >= currentIndex - window && b.PageIndex <= currentIndex + window)
            .ToList();
        var chunks = TextChunker.Chunk(document.DocumentId, inWindow);
        return RagContextService.FormatNumberedContext(chunks.Select(c => (document, c)).ToList(), maxChars);
    }
}
