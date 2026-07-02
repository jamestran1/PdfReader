namespace PdfReaderApp.Models;

/// <summary>Một không gian nghiên cứu: tham chiếu nhiều Document (many-to-many), sở hữu Notes của nó.
/// IsDefault = workspace mặc định của một tài liệu lẻ (ẩn khỏi danh sách, không xóa được);
/// DefaultDocumentId = tài liệu của default workspace đó (null với workspace người dùng tạo).</summary>
public sealed record Workspace(
    string Id,
    string Name,
    bool IsDefault,
    string? DefaultDocumentId,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs);
