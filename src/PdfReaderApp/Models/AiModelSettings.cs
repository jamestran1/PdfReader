namespace PdfReaderApp.Models;

/// <summary>Provider và tên model riêng cho Chat và cho Embedding (RAG).</summary>
public sealed record AiModelSettings(
    AiProvider ChatProvider, string ChatModel, AiProvider EmbeddingProvider, string EmbeddingModel)
{
    public static AiModelSettings Default { get; } = new(
        AiProvider.OpenAi, "gpt-4o-mini", AiProvider.OpenAi, "text-embedding-3-small");
}
