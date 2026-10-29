using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Adapter PDFium của <see cref="IDocumentLoader"/>: ôm dịch vụ PDF dùng chung cùng bộ phân tích cấu trúc.
/// Dịch vụ giữ một tài liệu tại một thời điểm nên mỗi lần nạp thay tài liệu trước đó.
/// </summary>
public sealed class PdfDocumentLoader : IDocumentLoader
{
    private readonly IPdfDocumentService _documentService;
    private readonly PdfStructureAnalyzer _analyzer;

    public PdfDocumentLoader(IPdfDocumentService documentService)
    {
        ArgumentNullException.ThrowIfNull(documentService);
        _documentService = documentService;
        _analyzer = new PdfStructureAnalyzer(documentService);
    }

    public IPdfDocumentService MatchSource => _documentService;

    public LoadedDocument Load(string path)
    {
        _documentService.LoadFile(path);
        var blocks = _analyzer.AnalyzeRich();
        var pageTexts = _documentService.ExtractPageTexts();
        return new LoadedDocument(DocumentId.FromFile(path), blocks, pageTexts);
    }
}
