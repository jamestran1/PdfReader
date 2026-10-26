using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Trích text bằng một <see cref="ITextPdfDocumentService"/> riêng cho mỗi lần gọi, độc lập với tài liệu
/// đang mở của viewer (<c>IPdfDocumentService</c> của MainViewModel giữ đúng một file và không dùng chung được).</summary>
public sealed class PdfFilePageTextSource : IPageTextSource
{
    public IReadOnlyList<PageText> Extract(string filePath)
    {
        using var document = new ITextPdfDocumentService();
        document.LoadFile(filePath);
        return document.ExtractPageTexts();
    }
}
