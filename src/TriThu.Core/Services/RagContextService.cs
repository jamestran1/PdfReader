using System.Linq;
using System.Text;
using Microsoft.Extensions.AI;
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
        if (_index.GetStatus(documentId, DocumentIndexingService.EmbeddingModel) != DocumentIndexStatus.Complete)
            return null;

        string? key = _settings.GetApiKey();
        if (string.IsNullOrEmpty(key)) return null;

        using var generator = _embedFactory.Create(key);
        var embeddings = await generator.GenerateAsync(new[] { question }, cancellationToken: ct);
        float[] queryVector = embeddings[0].Vector.ToArray();

        var chunks = _index.RetrieveRelevant(documentId, queryVector, k);
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
