using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

/// <summary>
/// Chuẩn hoá L2 mọi vector sinh ra. Gemini cắt chiều (MRL) từ 3072 xuống 768 nên vector không còn
/// độ dài 1, trong khi vec0 xếp hạng theo khoảng cách L2: không chuẩn hoá thì thứ hạng lệch theo độ dài.
/// </summary>
public sealed class NormalizingEmbeddingGenerator(IEmbeddingGenerator<string, Embedding<float>> inner)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
{
    public override async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var generated = await base.GenerateAsync(values, options, cancellationToken).ConfigureAwait(false);

        var normalized = new GeneratedEmbeddings<Embedding<float>>(generated.Count)
        {
            Usage = generated.Usage,
            AdditionalProperties = generated.AdditionalProperties,
        };
        foreach (var embedding in generated)
        {
            normalized.Add(new Embedding<float>(EmbeddingNormalizer.Normalize(embedding.Vector.Span))
            {
                CreatedAt = embedding.CreatedAt,
                ModelId = embedding.ModelId,
                AdditionalProperties = embedding.AdditionalProperties,
            });
        }

        return normalized;
    }
}
