namespace PdfReaderApp.Models;

/// <summary>Một ghi chú. v1: OwnerKey = DocumentId = documentId của sách; PageIndex = trang lúc tạo.
/// OwnerKey là phạm vi (sau này = workspaceId), DocumentId là anchor (doc mà note trỏ tới).</summary>
public sealed record Note(
    string Id,
    string OwnerKey,
    string? DocumentId,
    int? PageIndex,
    string Content,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs);
EOF 2>&1
