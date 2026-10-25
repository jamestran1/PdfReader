using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Tài liệu cần lập chỉ mục: mã, đường dẫn file và text từng trang đã trích.</summary>
public sealed record IndexingTarget(string DocumentId, string? FilePath, IReadOnlyList<PageText> PageTexts);
