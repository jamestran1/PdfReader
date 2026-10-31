using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Một lựa chọn kiểu Bản soạn trong hộp thoại: nhãn và một dòng mô tả.</summary>
public sealed record CompositionKindOption(CompositionKind Kind, string Label, string Description);
