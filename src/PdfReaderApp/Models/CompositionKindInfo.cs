namespace PdfReaderApp.Models;

/// <summary>Mọi thứ riêng của một kiểu Bản soạn: nhãn (chip, rail, tiêu đề dự phòng), mô tả ở hộp thoại tạo và phần chỉ dẫn
/// kiểu của system prompt. Thêm kiểu mới chỉ thêm một dòng vào <see cref="CompositionKinds.All"/>.</summary>
public sealed record CompositionKindInfo(CompositionKind Kind, string Label, string Description, string PromptInstructions);
