using Google.GenAI.Types;
using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

/// <summary>
/// Gắn <c>TaskType</c> của Gemini theo <see cref="EmbeddingPurpose"/>. Adapter Google.GenAI chỉ nhận
/// <c>EmbedContentConfig</c> qua <c>RawRepresentationFactory</c>, nên phải đặt ở đó thay vì ở option chung.
/// </summary>
public sealed class GeminiTaskTypeEmbeddingGenerator(
    IEmbeddingGenerator<string, Embedding<float>> inner, EmbeddingPurpose purpose)
    : DelegatingEmbeddingGenerator<string, Embedding<float>>(inner)
{
    public const string RetrievalDocument = "RETRIEVAL_DOCUMENT";
    public const string RetrievalQuery = "RETRIEVAL_QUERY";

    public string TaskType { get; } = purpose switch
    {
        EmbeddingPurpose.Document => RetrievalDocument,
        EmbeddingPurpose.Query => RetrievalQuery,
        _ => throw new ArgumentOutOfRangeException(nameof(purpose), purpose, "Mục đích embedding không được hỗ trợ."),
    };

    public override Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        // Clone để không làm đổi option của caller; giữ Dimensions và các option khác.
        var effective = options?.Clone() ?? new EmbeddingGenerationOptions();
        var callerFactory = effective.RawRepresentationFactory;
        effective.RawRepresentationFactory = generator =>
        {
            // Caller đã tự dựng config thì giữ nguyên, chỉ bổ sung TaskType khi chưa có.
            var config = callerFactory?.Invoke(generator) as EmbedContentConfig ?? new EmbedContentConfig();
            config.TaskType ??= TaskType;
            return config;
        };
        return base.GenerateAsync(values, effective, cancellationToken);
    }
}
