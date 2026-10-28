using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Kết quả bất biến của việc nạp một Document: định danh, cấu trúc (khối chữ) và chữ từng trang.
/// DocumentId do bộ nạp quyết định để test không cần file PDF thật để có định danh.
/// </summary>
public sealed record LoadedDocument(
    string DocumentId,
    IReadOnlyList<TextBlock> Blocks,
    IReadOnlyList<PageText> PageTexts);
