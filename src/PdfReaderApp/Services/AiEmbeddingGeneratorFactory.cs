using Microsoft.Extensions.AI;
using OpenAI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiEmbeddingGeneratorFactory : IEmbeddingGeneratorFactory
{
    public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => options.Provider switch
    {
        AiProvider.OpenAi => new OpenAIClient(options.ApiKey).GetEmbeddingClient(options.Model).AsIEmbeddingGenerator(),
        AiProvider.Gemini => CreateGemini(options),
        AiProvider.Claude => throw new NotSupportedException("Anthropic Claude không có API embedding."),
        _ => throw new NotSupportedException("Nhà cung cấp AI không được hỗ trợ."),
    };

    private static OwningEmbeddingGenerator CreateGemini(AiClientOptions options)
    {
        var client = GeminiClientFactory.Create(options.ApiKey);
        return new OwningEmbeddingGenerator(
            new NormalizingEmbeddingGenerator(
                client.AsIEmbeddingGenerator(options.Model, AiModelCatalog.GeminiEmbeddingDimensions)),
            client);
    }
}
