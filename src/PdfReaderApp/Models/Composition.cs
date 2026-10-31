namespace PdfReaderApp.Models;

/// <summary>
/// Bản soạn (xem GLOSSARY): văn bản AI sinh từ các Note của một Workspace.
/// <c>SourceNoteIds</c> theo đúng thứ tự đánh số: phần tử đầu là nguồn <c>[1]</c>.
/// </summary>
public sealed record Composition(
    string Id,
    string OwnerKey,
    string Title,
    CompositionKind Kind,
    string Content,
    string ChatModel,
    IReadOnlyList<string> SourceNoteIds,
    long CreatedAtUnixMs,
    string? AdditionalRequest = null);
