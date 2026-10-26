using Microsoft.Extensions.AI;
using OpenAI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiEmbeddingGeneratorFactory : IEmbeddingGeneratorFactory
{
    public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose) => options.Provider switch
    {
        // OpenAI không có khái niệm mục đích embedding nên bỏ qua purpose.
        AiProvider.OpenAi => new OpenAIClient(options.ApiKey).GetEmbeddingClient(options.Model).AsIEmbeddingGenerator(),
        AiProvider.Gemini => CreateGemini(options, purpose),
        AiProvider.Claude => throw new NotSupportedException("Anthropic Claude không có API embedding."),
        _ => throw new NotSupportedException("Nhà cung cấp AI không được hỗ trợ."),
    };

    private static OwningEmbeddingGenerator CreateGemini(AiClientOptions options, EmbeddingPurpose purpose)
    {
        var client = GeminiClientFactory.Create(options.ApiKey);
        return new OwningEmbeddingGenerator(
            new NormalizingEmbeddingGenerator(
                new GeminiTaskTypeEmbeddingGenerator(
                    client.AsIEmbeddingGenerator(options.Model, AiModelCatalog.GeminiEmbeddingDimensions), purpose)),
            client);
    }
}
