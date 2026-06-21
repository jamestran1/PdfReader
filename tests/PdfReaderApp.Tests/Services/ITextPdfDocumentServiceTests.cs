using System.IO;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PdfReaderApp.Models;
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

    // --- ExtractPageTexts tests ---

    [Fact]
    public void ExtractPageTexts_BeforeLoadFile_ThrowsInvalidOperationException()
    {
        using var service = new ITextPdfDocumentService();

        Assert.Throws<InvalidOperationException>((Action)(() => service.ExtractPageTexts()));
    }

    [Fact]
    public void ExtractPageTexts_TwoPagePdf_ReturnsTwoEntries()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Tiếng Việt rõ ràng", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            var pages = service.ExtractPageTexts();

            Assert.Equal(2, pages.Count);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExtractPageTexts_TwoPagePdf_HasCorrectZeroBasedPageIndexes()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Tiếng Việt rõ ràng", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            var pages = service.ExtractPageTexts();

            Assert.Equal(0, pages[0].PageIndex);
            Assert.Equal(1, pages[1].PageIndex);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExtractPageTexts_Page0_ContainsContiguousVietnameseText()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Tiếng Việt rõ ràng", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            var pages = service.ExtractPageTexts();

            // LocationTextExtractionStrategy must yield contiguous words — no spurious mid-word spaces
            Assert.Contains("Tiếng Việt", pages[0].Text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void ExtractPageTexts_Page1_ContainsExpectedText()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Tiếng Việt rõ ràng", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            var pages = service.ExtractPageTexts();

            Assert.Contains("Thiền", pages[1].Text);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    private static void CreateTwoPagePdf(string path, string page1Text, string page2Text)
    {
        using var writer = new PdfWriter(path);
        using var pdfDoc = new PdfDocument(writer);
        using var doc = new Document(pdfDoc);
        doc.Add(new Paragraph(page1Text));
        doc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
        doc.Add(new Paragraph(page2Text));
    }

    public void Dispose()
    {
        if (File.Exists(_tempPdf)) File.Delete(_tempPdf);
    }
}
