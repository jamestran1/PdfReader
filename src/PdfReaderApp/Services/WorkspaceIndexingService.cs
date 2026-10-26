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

    public async Task IndexDocumentAsync(WorkspaceDocument document, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(document);
        ct.ThrowIfCancellationRequested();

        string embeddingModel = _settings.GetModelSettings().EmbeddingModel;
        if (_index.GetStatus(document.DocumentId, embeddingModel) == DocumentIndexStatus.Complete) return;

        IReadOnlyList<PageText> pages = _index.GetChunks(document.DocumentId).Count > 0
            ? []
            : _pageTexts.Extract(document.FilePath);

        await _indexing.IndexAsync(document.DocumentId, document.FilePath, pages, progress: null, ct);
    }
}
