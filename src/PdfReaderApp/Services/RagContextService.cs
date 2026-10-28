using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

internal sealed class RagContextService
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

    /// <summary>Dựng ngữ cảnh từ các đoạn liên quan nhất của một Document, đánh số <c>[n]</c> như ngữ cảnh xuyên Workspace
    /// để câu trả lời trong phạm vi Tài liệu này cũng có trích dẫn. Null khi Document chưa <c>Complete</c>, thiếu key hoặc không có đoạn nào.</summary>
    public async Task<RagContext?> BuildDocumentContextAsync(
        WorkspaceDocument document, string question, int k = 5, int maxChars = ContextLimits.MaxChars, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        var models = _settings.GetModelSettings();
        if (_index.GetStatus(document.DocumentId, models.EmbeddingModel) != DocumentIndexStatus.Complete)
            return null;

        string? key = _settings.GetApiKey(models.EmbeddingProvider);
        if (string.IsNullOrEmpty(key)) return null;

        float[] queryVector = await EmbedQuestionAsync(models, key, question, ct);
        var chunks = _index.RetrieveRelevant(document.DocumentId, models.EmbeddingModel, queryVector, k);
        return FormatNumberedContext(chunks.Select(c => (document, c)).ToList(), maxChars);
    }

    /// <summary>Dựng ngữ cảnh xuyên Workspace: nhúng câu hỏi một lần, truy vấn từng Document đã <c>Complete</c> cho
    /// Embedding Model đang chọn, gộp lấy top-K chung theo khoảng cách rồi đánh số nguồn.
    /// Document chưa có index không chờ: được báo trong kết quả để nơi gọi lập chỉ mục nền.</summary>
    public Task<WorkspaceRagResult> BuildWorkspaceContextAsync(
        IReadOnlyList<WorkspaceDocument> documents, string question, WorkspaceRagOptions? options = null,
        CancellationToken ct = default)
        => BuildWorkspaceContextAsync(PlanWorkspaceIndexing(documents), question, options, ct);

    /// <summary>Dựng ngữ cảnh từ <paramref name="plan"/> đã phân loại. Tách khỏi việc phân loại để nơi gọi đưa tài liệu thiếu
    /// index vào hàng đợi nền TRƯỚC khi nhúng câu hỏi: nhúng lỗi không được làm mất việc lập chỉ mục.</summary>
    public async Task<WorkspaceRagResult> BuildWorkspaceContextAsync(
        WorkspaceIndexPlan plan, string question, WorkspaceRagOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        options ??= new WorkspaceRagOptions();
        var models = _settings.GetModelSettings();

        string? key = _settings.GetApiKey(models.EmbeddingProvider);
        if (plan.Ready.Count == 0 || string.IsNullOrEmpty(key))
            return new WorkspaceRagResult(null, [], plan.Unindexed, plan.Indexing, plan.MissingEmbeddingKeyProvider);

        float[] queryVector = await EmbedQuestionAsync(models, key, question, ct);

        var scored = new List<(WorkspaceDocument Document, ScoredChunk Hit)>();
        foreach (var document in plan.Ready)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var hit in _index.RetrieveRelevantScored(document.DocumentId, models.EmbeddingModel, queryVector, options.TopK))
                scored.Add((document, hit));
        }

        RagContext? context = FormatNumberedContext(
            scored.OrderBy(s => s.Hit.Distance).Take(options.TopK).Select(s => (s.Document, s.Hit.Chunk)).ToList(),
            options.MaxChars);
        return new WorkspaceRagResult(
            context?.Text, context?.Sources ?? [], plan.Unindexed, plan.Indexing, plan.MissingEmbeddingKeyProvider);
    }

    // Chỉ xét trạng thái của chính Embedding Model đang chọn: index Complete của model khác không tính là sẵn sàng.
    public WorkspaceIndexPlan PlanWorkspaceIndexing(IReadOnlyList<WorkspaceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var models = _settings.GetModelSettings();
        bool hasKey = !string.IsNullOrEmpty(_settings.GetApiKey(models.EmbeddingProvider));
        // TextOnly do thiếu key lúc lập chỉ mục thì nay có key nên lập lại được; TextOnly do kho vector không nạp được
        // thì lập lại cũng vô ích (và sẽ lặp lại mỗi câu hỏi).
        bool retryTextOnly = hasKey && _index.VectorSearchAvailable;
        var ready = new List<WorkspaceDocument>();
        var unindexed = new List<WorkspaceDocument>();
        var indexing = new List<WorkspaceDocument>();
        foreach (var document in documents)
        {
            var status = _index.GetStatus(document.DocumentId, models.EmbeddingModel);
            if (status == DocumentIndexStatus.Complete)
            {
                ready.Add(document);
            }
            else if (status == DocumentIndexStatus.Indexing)
            {
                indexing.Add(document);
            }
            // Không có key thì không lập chỉ mục được: không xếp hàng, nơi gọi báo thiếu key.
            else if (hasKey && (status is DocumentIndexStatus.None or DocumentIndexStatus.Partial
                || (status == DocumentIndexStatus.TextOnly && retryTextOnly)))
            {
                unindexed.Add(document);
            }
            // Empty (không có text): lập chỉ mục lại cũng không đổi kết quả.
        }

        return new WorkspaceIndexPlan(ready, unindexed, indexing, hasKey ? null : models.EmbeddingProvider);
    }

    private async Task<float[]> EmbedQuestionAsync(AiModelSettings models, string key, string question, CancellationToken ct)
    {
        using var generator = _embedFactory.Create(
            new AiClientOptions(models.EmbeddingProvider, models.EmbeddingModel, key), EmbeddingPurpose.Query);
        var embeddings = await generator.GenerateAsync(new[] { question }, cancellationToken: ct);
        return embeddings[0].Vector.ToArray();
    }

    // Hàm thuần: đánh số theo thứ tự đã sắp, dừng khi vượt ngân sách ký tự (nguồn bị bỏ cũng không vào bảng nguồn).
    public static RagContext? FormatNumberedContext(
        IReadOnlyList<(WorkspaceDocument Document, Chunk Chunk)> ranked, int maxChars)
    {
        var sb = new StringBuilder();
        var sources = new List<RagSource>();
        foreach (var (document, chunk) in ranked)
        {
            int number = sources.Count + 1;
            string entry = string.Create(
                System.Globalization.CultureInfo.InvariantCulture,
                $"[{number}] Tài liệu: \"{document.Title}\", Trang: {chunk.PageIndex + 1}\n{chunk.Text}");
            int separator = sb.Length > 0 ? EntrySeparator.Length : 0;
            if (sb.Length + separator + entry.Length > maxChars) break;
            if (separator > 0) sb.Append(EntrySeparator);
            sb.Append(entry);
            sources.Add(new RagSource(number, document.DocumentId, document.Title, chunk.PageIndex, chunk.Text));
        }

        return sources.Count == 0 ? null : new RagContext(sb.ToString(), sources);
    }
}
