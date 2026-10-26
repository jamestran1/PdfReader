using Microsoft.Extensions.AI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class DocumentIndexingService
{
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
        string documentId, string? filePath, IReadOnlyList<PageText> pages,
        IProgress<IndexingProgress>? progress, CancellationToken ct)
    {
        var models = _settings.GetModelSettings();
        string embeddingModel = models.EmbeddingModel;

        // Already fully indexed with the current embedding model → reuse, skip re-embedding.
        if (_index.GetStatus(documentId, embeddingModel) == DocumentIndexStatus.Complete)
        {
            progress?.Report(new IndexingProgress(0, 0, "complete"));
            return;
        }

        // Giành quyền trước khi đụng tới chunk: tài liệu đang mở và hàng đợi Workspace có thể cùng muốn lập chỉ mục
        // một tài liệu; lượt thua bỏ qua để không ghi chunk chồng nhau (mồ côi vector) và không nhúng hai lần.
        if (!_index.TryBeginIndexing(documentId, embeddingModel, out long claimToken)) return;

        try
        {
            await IndexClaimedAsync(documentId, filePath, pages, progress, new ClaimedRun(embeddingModel, models, claimToken), ct);
        }
        catch (Exception)
        {
            // Mọi lối thoát lỗi hoặc huỷ phải nhả claim: để kẹt ở Indexing thì tài liệu không bao giờ được lập lại.
            // Chỉ nhả claim của chính mình: lập chỉ mục lại (xoá rồi chạy lượt mới) có thể đã giao claim cho lượt khác.
            _index.ReleaseIndexing(documentId, embeddingModel, claimToken);
            throw;
        }
    }

    // Một lượt lập chỉ mục đã giành claim: model, cấu hình và token để mọi lần ghi sau đó kiểm claim còn của mình.
    private sealed record ClaimedRun(string EmbeddingModel, AiModelSettings Models, long ClaimToken);

    // Lượt mất claim giữa chừng (tài liệu bị xoá để lập chỉ mục lại, lượt khác đã giành claim) dừng im lặng, như lượt thua ở
    // đầu IndexAsync: không ghi thêm vector mồ côi, không đè trạng thái của lượt mới, không báo thành công hay lỗi.
    private async Task IndexClaimedAsync(
        string documentId, string? filePath, IReadOnlyList<PageText> pages,
        IProgress<IndexingProgress>? progress, ClaimedRun run, CancellationToken ct)
    {
        string embeddingModel = run.EmbeddingModel;
        AiModelSettings models = run.Models;
        long claimToken = run.ClaimToken;

        // Chunk đã có (do model khác lập chỉ mục trước) thì dùng lại, không ghi đè và không xoá vector model khác.
        var stored = LoadOrWriteChunks(documentId, filePath, pages);

        if (stored.Count == 0)
        {
            _index.FinishIndexing(documentId, embeddingModel, claimToken, DocumentIndexStatus.Empty);
            return;
        }

        // Không có kho vector thì WriteEmbeddings bỏ qua; đánh dấu Complete sẽ không bao giờ nhúng lại khi vec0 nạp được.
        if (!_index.VectorSearchAvailable)
        {
            _index.FinishIndexing(documentId, embeddingModel, claimToken, DocumentIndexStatus.TextOnly);
            return;
        }

        progress?.Report(new IndexingProgress(0, stored.Count, "indexing"));

        string? key = _settings.GetApiKey(models.EmbeddingProvider);
        if (string.IsNullOrEmpty(key))
        {
            _index.FinishIndexing(documentId, embeddingModel, claimToken, DocumentIndexStatus.TextOnly);
            return;
        }

        // Xoá vector dở dang của chính model này (Partial) trước khi nhúng lại; vector model khác giữ nguyên.
        _index.DeleteEmbeddings(documentId, embeddingModel);

        using var generator = _embedFactory.Create(new AiClientOptions(models.EmbeddingProvider, embeddingModel, key), EmbeddingPurpose.Document);
        int? done = await EmbedAllBatchesAsync(generator, documentId, run, stored, progress, ct);
        if (done is null) return;

        if (!_index.FinishIndexing(documentId, embeddingModel, claimToken, DocumentIndexStatus.Complete)) return;
        progress?.Report(new IndexingProgress(done.Value, stored.Count, "complete"));
    }

    private IReadOnlyList<StoredChunk> LoadOrWriteChunks(
        string documentId, string? filePath, IReadOnlyList<PageText> pages)
    {
        var stored = _index.GetChunks(documentId);
        if (stored.Count > 0) return stored;

        var fresh = TextChunker.ChunkPages(documentId, pages);
        int pageCount = pages.Count == 0 ? 0 : pages.Max(p => p.PageIndex) + 1;
        var freshIds = _index.WriteChunks(documentId, filePath, pageCount, fresh);
        return [.. freshIds.Select((id, i) => new StoredChunk(id, fresh[i].Text))];
    }

    // Trả null khi lượt mất claim giữa chừng (lô vừa nhúng bị bỏ, không ghi).
    private async Task<int?> EmbedAllBatchesAsync(
        IEmbeddingGenerator<string, Embedding<float>> generator, string documentId, ClaimedRun run,
        IReadOnlyList<StoredChunk> chunks, IProgress<IndexingProgress>? progress, CancellationToken ct)
    {
        int done = 0;
        for (int i = 0; i < chunks.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batch = chunks.Skip(i).Take(BatchSize).ToList();

            var embeddings = await generator.GenerateAsync(
                batch.Select(c => c.Text), cancellationToken: ct);

            var pairs = new List<(long, float[])>();
            for (int j = 0; j < batch.Count; j++)
                pairs.Add((batch[j].ChunkId, embeddings[j].Vector.ToArray()));

            // Kiểm claim và ghi nguyên tử trong index: không có khe hở để lượt đã mất claim ghi vector mồ côi.
            if (!_index.WriteEmbeddingsIfClaimed(documentId, run.EmbeddingModel, run.ClaimToken, pairs)) return null;
            done += batch.Count;
            progress?.Report(new IndexingProgress(done, chunks.Count, "indexing"));
        }

        return done;
    }
}
