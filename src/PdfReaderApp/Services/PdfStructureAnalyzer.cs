using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public class PdfStructureAnalyzer
{
    // Kept for backward-compat; remove after all callers migrate to AnalyzeRich()
    public class TextChunk
    {
        public string Text { get; set; } = string.Empty;
        public int PageIndex { get; set; }
        public string StructureType { get; set; } = "Paragraph";
    }

    private readonly IPdfDocumentService _documentService;

    public PdfStructureAnalyzer(IPdfDocumentService documentService)
    {
        _documentService = documentService;
    }

    public List<TextChunk> Analyze() =>
        _documentService.ExtractStructure()
            .Select(b => new TextChunk
            {
                Text = b.Text,
                PageIndex = b.PageIndex,
                StructureType = b.StructureType
            })
            .ToList();

    public List<TextBlock> AnalyzeRich() => _documentService.ExtractStructure();
}
