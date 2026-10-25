using PdfReaderApp.Services;

namespace PdfReaderApp.Models;

/// <summary>Provider và tên model riêng cho Chat và cho Embedding (RAG).</summary>
public sealed record AiModelSettings(
    AiProvider ChatProvider, string ChatModel, AiProvider EmbeddingProvider, string EmbeddingModel)
{
    /// <summary>Mặc định lấy từ <see cref="AiModelCatalog"/> để chỉ có một nguồn sự thật.</summary>
    public static AiModelSettings Default { get; } = new(
        AiProvider.OpenAi, AiModelCatalog.DefaultChatModel(AiProvider.OpenAi),
        AiProvider.OpenAi, AiModelCatalog.DefaultEmbeddingModel(AiProvider.OpenAi));
}
