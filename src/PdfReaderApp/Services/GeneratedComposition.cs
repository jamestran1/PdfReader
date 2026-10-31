namespace PdfReaderApp.Services;

/// <summary>Kết quả AI sinh một Bản soạn: tiêu đề do AI đặt, nội dung Markdown và Chat Model đã dùng.</summary>
public sealed record GeneratedComposition(string Title, string Content, string ChatModel);
