using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Trích text từng trang của một file PDF không cần mở nó trong viewer (tài liệu của Workspace chưa mở tab).</summary>
public interface IPageTextSource
{
    IReadOnlyList<PageText> Extract(string filePath);
}
