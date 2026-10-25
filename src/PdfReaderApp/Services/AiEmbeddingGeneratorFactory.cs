using Microsoft.Extensions.AI;
using OpenAI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class AiEmbeddingGeneratorFactory : IEmbeddingGeneratorFactory
{
    public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => options.Provider switch
    {
        AiProvider.OpenAi => new OpenAIClient(options.ApiKey).GetEmbeddingClient(options.Model).AsIEmbeddingGenerator(),
        AiProvider.Gemini => throw new NotSupportedException("Embedding qua Google Gemini chưa được hỗ trợ."),
        AiProvider.Claude => throw new NotSupportedException("Anthropic Claude không có API embedding."),
        _ => throw new NotSupportedException("Nhà cung cấp AI không được hỗ trợ."),
    };
}
