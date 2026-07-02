using Microsoft.Extensions.AI;
using OpenAI;

namespace PdfReaderApp.Services;

public sealed class OpenAiEmbeddingGeneratorFactory : IEmbeddingGeneratorFactory
{
    private const string Model = "text-embedding-3-small";

    public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) =>
        new OpenAIClient(apiKey).GetEmbeddingClient(Model).AsIEmbeddingGenerator();
}
