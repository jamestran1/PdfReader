using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Sinh Bản soạn từ ngữ cảnh Note. Ném <see cref="AiChatException"/> khi gọi AI lỗi; huỷ thì ném <see cref="OperationCanceledException"/>.</summary>
public interface ICompositionGenerator
{
    Task<GeneratedComposition> GenerateAsync(CompositionKind kind, CompositionContext context, CancellationToken cancellationToken);
}
