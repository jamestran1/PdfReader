using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lập chỉ mục một Document của Workspace mà không cần mở nó. Rẻ nhất có thể: đã Complete cho model đang chọn
/// thì bỏ qua; đã có chunk (model khác lập trước) thì dùng lại, không trích text; chỉ khi chưa có chunk mới trích text
/// từ file. Phần nhúng vector do <see cref="DocumentIndexingService"/> làm.</summary>
public sealed class WorkspaceIndexingService
{
    private readonly DocumentIndexingService _indexing;
    private readonly IDocumentIndex _index;
    private readonly IPageTextSource _pageTexts;
    private readonly ISettingsService _settings;

    public WorkspaceIndexingService(
        DocumentIndexingService indexing, IDocumentIndex index, IPageTextSource pageTexts, ISettingsService settings)
    {
        _indexing = indexing;
        _index = index;
        _pageTexts = pageTexts;
        _settings = settings;
    }

    /// <summary>Lập chỉ mục <paramref name="document"/> và trả trạng thái index của model đang chọn sau đó:
    /// <c>Complete</c> là thành công; <c>Indexing</c> nghĩa là lượt khác đang làm nên lượt này bỏ qua;
    /// các trạng thái còn lại (TextOnly, Empty, Partial) nghĩa là chưa dùng được cho RAG.</summary>
    public async Task<DocumentIndexStatus> IndexDocumentAsync(WorkspaceDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();

        string embeddingModel = _settings.GetModelSettings().EmbeddingModel;
        if (_index.GetStatus(document.DocumentId, embeddingModel) == DocumentIndexStatus.Complete)
            return DocumentIndexStatus.Complete;

        await _indexing.IndexAsync(document.DocumentId, document.FilePath, PagesToIndex(document), progress: null, ct);
        return _index.GetStatus(document.DocumentId, embeddingModel);
    }

    // Truyền danh sách rỗng khi chunk đã có là có chủ đích: DocumentIndexingService.IndexAsync dùng lại chunk đã lưu
    // (GetChunks) và bỏ qua tham số pages, nên không cần tốn công trích text từ file.
    private IReadOnlyList<PageText> PagesToIndex(WorkspaceDocument document)
    {
        bool chunksAlreadyStored = _index.GetChunks(document.DocumentId).Count > 0;
        return chunksAlreadyStored ? [] : _pageTexts.Extract(document.FilePath);
    }
}
