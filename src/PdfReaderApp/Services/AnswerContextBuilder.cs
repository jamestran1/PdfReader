using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Dựng Ngữ cảnh trả lời cho một câu hỏi: chọn đường theo Phạm vi, xếp hàng lập chỉ mục, truy hồi RAG
/// và lùi về cửa sổ trang khi RAG không dùng được. Chính sách này nằm ở đây để khung chat chỉ còn hiển thị và stream.</summary>
internal sealed class AnswerContextBuilder
{
    private const int ContextPageWindow = 2;

    private readonly RagContextService _retrieval;
    private readonly IIndexingQueue _indexingQueue;

    public AnswerContextBuilder(RagContextService retrieval, IIndexingQueue indexingQueue)
    {
        _retrieval = retrieval;
        _indexingQueue = indexingQueue;
    }

    /// <summary>Dựng builder với dịch vụ truy hồi mặc định; để nơi lắp ráp không phải biết tới <see cref="RagContextService"/>.</summary>
    public static AnswerContextBuilder Create(
        IDocumentIndex index, IEmbeddingGeneratorFactory embeddingFactory, ISettingsService settings, IIndexingQueue indexingQueue)
        => new(new RagContextService(index, embeddingFactory, settings), indexingQueue);

    /// <summary>Dựng ngữ cảnh cho <paramref name="request"/>. Lỗi RAG không ném ra mà lùi về ngữ cảnh theo trang;
    /// chỉ huỷ có chủ đích (<paramref name="ct"/>) mới ném <see cref="OperationCanceledException"/>.</summary>
    public async Task<AnswerContext> BuildAsync(AnswerContextRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var notices = new List<AnswerContextNotice>();
        RagContext? rag = null;
        try
        {
            rag = request.Scope == DocumentScope.Workspace
                ? await BuildWorkspaceRagAsync(request, notices, ct)
                : await BuildSingleDocumentRagAsync(request, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
#pragma warning disable CA1031 // degrade: RAG lỗi thì lùi về ngữ cảnh theo trang
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"AnswerContextBuilder: RAG lỗi: {ex}");
            if (request.Scope == DocumentScope.Workspace) notices.Add(new AnswerContextNotice.PageOnlyFallback());
        }
#pragma warning restore CA1031

        RagContext context = rag ?? BuildPageContext(request.Snapshot);
        return new AnswerContext(context.Text, context.Sources, notices);
    }

    // Không có DocumentId thì CitationOpener không có nơi nhảy tới: giữ ngữ cảnh liền khối, không đánh số.
    private static RagContext BuildPageContext(DocumentSnapshot snapshot)
    {
        if (snapshot.DocumentId is not null)
        {
            var document = new WorkspaceDocument(snapshot.DocumentId, snapshot.DocumentTitle, FilePath: string.Empty);
            RagContext? numbered = PageContextBuilder.BuildNumberedAround(
                document, snapshot.Blocks, snapshot.CurrentPage, ContextPageWindow);
            if (numbered is not null) return numbered;
        }

        return new RagContext(
            DocumentContextBuilder.BuildAround(snapshot.Blocks, snapshot.CurrentPage, ContextPageWindow), []);
    }

    private async Task<RagContext?> BuildSingleDocumentRagAsync(AnswerContextRequest request, CancellationToken ct)
    {
        DocumentSnapshot snapshot = request.Snapshot;
        if (snapshot.DocumentId is null) return null;
        // FilePath chỉ dùng để lập chỉ mục nền, việc này không có ở phạm vi Tài liệu này.
        var document = new WorkspaceDocument(snapshot.DocumentId, snapshot.DocumentTitle, FilePath: string.Empty);
        return await _retrieval.BuildDocumentContextAsync(document, request.Question, ct: ct);
    }

    // Câu hỏi không chờ tài liệu chưa index: chỉ dùng tài liệu đã Complete, phần còn lại đưa vào lập chỉ mục nền.
    private async Task<RagContext?> BuildWorkspaceRagAsync(
        AnswerContextRequest request, List<AnswerContextNotice> notices, CancellationToken ct)
    {
        var plan = _retrieval.PlanWorkspaceIndexing(request.WorkspaceDocuments);

        // Xếp hàng lập chỉ mục TRƯỚC khi nhúng câu hỏi: nhúng lỗi (mạng, key) không được làm mất việc lập chỉ mục.
        if (plan.Unindexed.Count > 0) _indexingQueue.Enqueue(plan.Unindexed);

        // Tài liệu đang lập chỉ mục ở nơi khác (vd tài liệu đang mở) chỉ được nêu một lần cho câu hỏi này, không giữ lâu dài:
        // không có sự kiện nào báo khi chúng xong nên một con số lưu lại sẽ cũ đi. Tài liệu runner đang giữ claim thì đã có
        // trong StatusText của runner nên không đếm lại.
        int elsewhere = plan.Indexing.Count(d => !_indexingQueue.IsQueued(d.DocumentId));
        if (elsewhere > 0) notices.Add(new AnswerContextNotice.IndexingElsewhere(elsewhere));

        if (plan.MissingEmbeddingKeyProvider is { } provider)
        {
            notices.Add(new AnswerContextNotice.MissingEmbeddingKey(provider));
            return null;
        }

        WorkspaceRagResult result = await _retrieval.BuildWorkspaceContextAsync(plan, request.Question, ct: ct);
        if (result.Context is null)
        {
            notices.Add(new AnswerContextNotice.PageOnlyFallback());
            return null;
        }

        return new RagContext(result.Context, result.Sources);
    }
}
