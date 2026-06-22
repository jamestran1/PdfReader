namespace PdfReaderApp.Models;

/// <summary>Một tài liệu trong thư viện (đã copy vào thư mục app). Thời gian là unix seconds.</summary>
public sealed record LibraryItem(
    string DocumentId,
    string Title,
    string StoredPath,
    string? ThumbPath,
    int PageCount,
    long ImportedAtUnix,
    long LastOpenedAtUnix);
