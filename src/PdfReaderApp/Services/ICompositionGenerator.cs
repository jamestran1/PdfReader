using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Kết quả AI sinh một Bản soạn: tiêu đề do AI đặt, nội dung Markdown và Chat Model đã dùng.</summary>
internal sealed record GeneratedComposition(string Title, string Content, string ChatModel);

/// <summary>Sinh Bản soạn từ ngữ cảnh Note. Ném <see cref="AiChatException"/> khi gọi AI lỗi; huỷ thì ném <see cref="OperationCanceledException"/>.</summary>
internal interface ICompositionGenerator
{
    Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken);
}
