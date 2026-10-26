using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public interface IEmbeddingGeneratorFactory
{
    IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose);
}
