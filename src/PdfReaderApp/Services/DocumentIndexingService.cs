using System.Linq;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class DocumentIndexingService
{
    public const string EmbeddingModel = "text-embedding-3-small";
    private const int BatchSize = 100;

    private readonly IDocumentIndex _index;
    private readonly IEmbeddingGeneratorFactory _embedFactory;
    private readonly ISettingsService _settings;

    public DocumentIndexingService(
        IDocumentIndex index, IEmbeddingGeneratorFactory embedFactory, ISettingsService settings)
    {
        _index = index;
        _embedFactory = embedFactory;
        _settings = settings;
    }

    public async Task IndexAsync(
        string documentId, string? filePath, IReadOnlyList<TextBlock> blocks,
        IProgress<IndexingProgress>? progress, CancellationToken ct)
    {
        var chunks = TextChunker.Chunk(documentId, blocks);
        int pageCount = blocks.Count == 0 ? 0 : blocks.Max(b => b.PageIndex) + 1;

        if (chunks.Count == 0)
        {
            _index.WriteChunks(documentId, filePath, pageCount, chunks);
            _index.SetStatus(documentId, DocumentIndexStatus.Empty, EmbeddingModel);
            return;
        }

        var chunkIds = _index.WriteChunks(documentId, filePath, pageCount, chunks);
        progress?.Report(new IndexingProgress(0, chunks.Count, "indexing"));

        string? key = _settings.GetApiKey();
        if (string.IsNullOrEmpty(key))
        {
            _index.SetStatus(documentId, DocumentIndexStatus.TextOnly, EmbeddingModel);
            return;
        }

        using var generator = _embedFactory.Create(key);
        int done = 0;

        try
        {
            for (int i = 0; i < chunks.Count; i += BatchSize)
            {
                ct.ThrowIfCancellationRequested();

                var batchChunks = chunks.Skip(i).Take(BatchSize).ToList();
                var batchIds = chunkIds.Skip(i).Take(BatchSize).ToList();

                var embeddings = await generator.GenerateAsync(
                    batchChunks.Select(c => c.Text), cancellationToken: ct);

                var pairs = new List<(long, float[])>();
                for (int j = 0; j < batchChunks.Count; j++)
                    pairs.Add((batchIds[j], embeddings[j].Vector.ToArray()));

                _index.WriteEmbeddings(pairs);
                done += batchChunks.Count;
                progress?.Report(new IndexingProgress(done, chunks.Count, "indexing"));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _index.SetStatus(documentId, DocumentIndexStatus.Partial, EmbeddingModel);
            throw;
        }

        _index.SetStatus(documentId, DocumentIndexStatus.Complete, EmbeddingModel);
        progress?.Report(new IndexingProgress(done, chunks.Count, "complete"));
    }
}
