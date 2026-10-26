using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class RagContextService
{
    private readonly IDocumentIndex _index;
    private readonly IEmbeddingGeneratorFactory _embedFactory;
    private readonly ISettingsService _settings;

    public RagContextService(
        IDocumentIndex index, IEmbeddingGeneratorFactory embedFactory, ISettingsService settings)
    {
        _index = index;
        _embedFactory = embedFactory;
        _settings = settings;
    }

    public async Task<string?> BuildContextAsync(
        string documentId, string question, int k = 5, int maxChars = 48000, CancellationToken ct = default)
    {
        var models = _settings.GetModelSettings();
        if (_index.GetStatus(documentId, models.EmbeddingModel) != DocumentIndexStatus.Complete)
            return null;

        string? key = _settings.GetApiKey(models.EmbeddingProvider);
        if (string.IsNullOrEmpty(key)) return null;

        using var generator = _embedFactory.Create(
            new AiClientOptions(models.EmbeddingProvider, models.EmbeddingModel, key), EmbeddingPurpose.Query);
        var embeddings = await generator.GenerateAsync(new[] { question }, cancellationToken: ct);
        float[] queryVector = embeddings[0].Vector.ToArray();

        var chunks = _index.RetrieveRelevant(documentId, models.EmbeddingModel, queryVector, k);
        if (chunks.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var c in chunks)
        {
            string line = $"[Trang {c.PageIndex + 1}] {c.Text}";
            if (sb.Length + line.Length > maxChars) break;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }
}
