using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IPdfDocumentService : IDisposable
{
    void LoadFile(string filePath);
    List<TextBlock> ExtractStructure();
    List<PageText> ExtractPageTexts();
}
