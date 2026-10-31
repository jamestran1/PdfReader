using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Lựa chọn từ hộp thoại Tạo Bản soạn: kiểu, id Note nguồn (thứ tự cây) và yêu cầu thêm (null nếu để trống).</summary>
public sealed record CompositionDraft(CompositionKind Kind, IReadOnlyList<string> NoteIds, string? AdditionalRequest);
