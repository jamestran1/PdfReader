namespace PdfReaderApp.Models;

/// <summary>Mot tai lieu trong thu vien (da copy vao thu muc app). Thoi gian la unix seconds.</summary>
public sealed record LibraryItem(
    string DocumentId,
    string Title,
    string StoredPath,
    string? ThumbPath,
    int PageCount,
    long ImportedAtUnix,
    long LastOpenedAtUnix);
