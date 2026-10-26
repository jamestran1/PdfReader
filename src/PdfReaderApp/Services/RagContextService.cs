using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class RagContextService
{
    private const string EntrySeparator = "\n\n";

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

    /// <summary>Dựng ngữ cảnh xuyên Workspace: nhúng câu hỏi một lần, truy vấn từng Document đã <c>Complete</c> cho
    /// Embedding Model đang chọn, gộp lấy top-<paramref name="k"/> chung theo khoảng cách rồi đánh số nguồn.
    /// Document chưa có index không chờ: được báo trong kết quả để nơi gọi lập chỉ mục nền.</summary>
    public async Task<WorkspaceRagResult> BuildWorkspaceContextAsync(
        IReadOnlyList<WorkspaceDocument> documents, string question, int k = 8, int maxChars = 48000,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var models = _settings.GetModelSettings();
        var plan = PartitionByIndexStatus(documents, models.EmbeddingModel);

        string? key = _settings.GetApiKey(models.EmbeddingProvider);
        if (plan.Ready.Count == 0 || string.IsNullOrEmpty(key))
            return new WorkspaceRagResult(null, [], plan.Unindexed, plan.Indexing);

        float[] queryVector = await EmbedQuestionAsync(models, key, question, ct);

        var scored = new List<(WorkspaceDocument Document, ScoredChunk Hit)>();
        foreach (var document in plan.Ready)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var hit in _index.RetrieveRelevantScored(document.DocumentId, models.EmbeddingModel, queryVector, k))
                scored.Add((document, hit));
        }

        var (context, sources) = FormatNumberedContext(
            scored.OrderBy(s => s.Hit.Distance).Take(k).ToList(), maxChars);
        return new WorkspaceRagResult(context, sources, plan.Unindexed, plan.Indexing);
    }

    private sealed record IndexPlan(
        IReadOnlyList<WorkspaceDocument> Ready,
        IReadOnlyList<WorkspaceDocument> Unindexed,
        IReadOnlyList<WorkspaceDocument> Indexing);

    // Chỉ xét trạng thái của chính Embedding Model đang chọn: index Complete của model khác không tính là sẵn sàng.
    private IndexPlan PartitionByIndexStatus(IReadOnlyList<WorkspaceDocument> documents, string embeddingModel)
    {
        var ready = new List<WorkspaceDocument>();
        var unindexed = new List<WorkspaceDocument>();
        var indexing = new List<WorkspaceDocument>();
        foreach (var document in documents)
        {
            switch (_index.GetStatus(document.DocumentId, embeddingModel))
            {
                case DocumentIndexStatus.Complete: ready.Add(document); break;
                case DocumentIndexStatus.None or DocumentIndexStatus.Partial: unindexed.Add(document); break;
                case DocumentIndexStatus.Indexing: indexing.Add(document); break;
                // TextOnly (không có key/kho vector) và Empty (không có text): lập chỉ mục lại cũng không đổi kết quả.
                default: break;
            }
        }

        return new IndexPlan(ready, unindexed, indexing);
    }

    private async Task<float[]> EmbedQuestionAsync(AiModelSettings models, string key, string question, CancellationToken ct)
    {
        using var generator = _embedFactory.Create(
            new AiClientOptions(models.EmbeddingProvider, models.EmbeddingModel, key), EmbeddingPurpose.Query);
        var embeddings = await generator.GenerateAsync(new[] { question }, cancellationToken: ct);
        return embeddings[0].Vector.ToArray();
    }

    // Hàm thuần: đánh số theo thứ tự đã sắp, dừng khi vượt ngân sách ký tự (nguồn bị bỏ cũng không vào bảng nguồn).
    private static (string? Context, IReadOnlyList<RagSource> Sources) FormatNumberedContext(
        IReadOnlyList<(WorkspaceDocument Document, ScoredChunk Hit)> ranked, int maxChars)
    {
        var sb = new StringBuilder();
        var sources = new List<RagSource>();
        foreach (var (document, hit) in ranked)
        {
            int number = sources.Count + 1;
            string entry = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[{number}] Tài liệu: \"{document.Title}\", Trang: {hit.Chunk.PageIndex + 1}\n{hit.Chunk.Text}");
            int separator = sb.Length > 0 ? EntrySeparator.Length : 0;
            if (sb.Length + separator + entry.Length > maxChars) break;
            if (separator > 0) sb.Append(EntrySeparator);
            sb.Append(entry);
            sources.Add(new RagSource(number, document.DocumentId, document.Title, hit.Chunk.PageIndex));
        }

        return (sources.Count == 0 ? null : sb.ToString(), sources);
    }
}
