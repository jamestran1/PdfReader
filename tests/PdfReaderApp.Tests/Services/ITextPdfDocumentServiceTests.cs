using System.IO;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class ITextPdfDocumentServiceTests : IDisposable
{
    private readonly string _tempPdf;

    public ITextPdfDocumentServiceTests()
    {
        _tempPdf = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        CreateTestPdf(_tempPdf, "Hello World", "Second line of text");
    }

    private static void CreateTestPdf(string path, params string[] lines)
    {
        using var writer = new PdfWriter(path);
        using var pdfDoc = new PdfDocument(writer);
        using var doc = new Document(pdfDoc);
        foreach (var line in lines)
            doc.Add(new Paragraph(line));
    }

    [Fact]
    public void ExtractStructure_KnownPdf_ReturnsNonEmptyBlocks()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();

        Assert.NotEmpty(blocks);
    }

    [Fact]
    public void ExtractStructure_KnownPdf_ContainsExpectedText()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();
        var allText = string.Join(" ", blocks.Select(b => b.Text));

        Assert.Contains("Hello World", allText);
    }

    [Fact]
    public void ExtractStructure_KnownPdf_BlocksHaveCorrectPageIndex()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();

        Assert.All(blocks, b => Assert.Equal(0, b.PageIndex));
    }

    [Fact]
    public void ExtractStructure_KnownPdf_BlocksHaveNonNegativeCoordinates()
    {
        using var service = new ITextPdfDocumentService();
        service.LoadFile(_tempPdf);

        var blocks = service.ExtractStructure();

        Assert.All(blocks, b =>
        {
            Assert.True(b.PdfX >= 0f, $"PdfX={b.PdfX} should be >= 0");
            Assert.True(b.PdfY >= 0f, $"PdfY={b.PdfY} should be >= 0");
        });
    }

    [Fact]
    public void ExtractStructure_BeforeLoadFile_ThrowsInvalidOperationException()
    {
        using var service = new ITextPdfDocumentService();

        Assert.Throws<InvalidOperationException>((Action)(() => service.ExtractStructure()));
    }

    public void Dispose()
    {
        if (File.Exists(_tempPdf)) File.Delete(_tempPdf);
    }
}
