using System.IO;
using iText.IO.Font;
using iText.Kernel.Font;
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

    [Fact]
    public void FindMatchRects_AccentInsensitivePhrase_ReturnsPositiveRect()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Trước khi đi kinh hành ta phải xác định", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            // Folded query (no diacritics) must still locate the accented phrase on the page.
            var rects = service.FindMatchRects(0, "kinh hanh");

            Assert.NotEmpty(rects);
            Assert.All(rects, r =>
            {
                Assert.True(r.Width > 0f, $"width={r.Width}");
                Assert.True(r.Height > 0f, $"height={r.Height}");
            });
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FindMatchRects_AbsentPhrase_ReturnsEmpty()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Trước khi đi kinh hành", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            Assert.Empty(service.FindMatchRects(0, "ky sinh trung"));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FindPassageRects_FragmentWrappingOntoNextLine_ReturnsOneRectPerLine()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Trước khi đi kinh hành ta phải\nxác định rõ mục tiêu", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            var rects = service.FindPassageRects(0, ["kinh hanh ta phai xac dinh"]);

            // Một hộp bao cả hai dòng sẽ tô cả phần đầu dòng 1 và đuôi dòng 2 không thuộc đoạn nguồn.
            Assert.Equal(2, rects.Count);
            Assert.NotEqual(rects[0].PdfY, rects[1].PdfY);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FindPassageRects_OneFragmentMissingFromPage_StillLocatesTheOthers()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateTwoPagePdf(path, "Trước khi đi kinh hành ta phải xác định", "Thiền định");
            using var service = new ITextPdfDocumentService();
            service.LoadFile(path);

            Assert.Single(service.FindPassageRects(0, ["ky sinh trung", "kinh hanh"]));
            Assert.Empty(service.FindPassageRects(0, []));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void FindRects_PageWithCropBoxOffsetFromMediaBox_AreRelativeToCropBoxOrigin()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        string uncroppedPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        try
        {
            CreateCroppedPdf(path, new iText.Kernel.Geom.Rectangle(20, 30, 500, 700));
            CreateCroppedPdf(uncroppedPath, new iText.Kernel.Geom.Rectangle(0, 0, 600, 800));
            using var cropped = new ITextPdfDocumentService();
            cropped.LoadFile(path);
            using var uncropped = new ITextPdfDocumentService();
            uncropped.LoadFile(uncroppedPath);

            var expected = Assert.Single(uncropped.FindMatchRects(0, "kinh hanh"));
            var match = Assert.Single(cropped.FindMatchRects(0, "kinh hanh"));
            var passage = Assert.Single(cropped.FindPassageRects(0, ["kinh hanh"]));

            // PDFium vẽ theo CropBox, nên gốc toạ độ của hộp phải là góc dưới trái của CropBox (20, 30).
            foreach (var r in new[] { match, passage })
            {
                Assert.Equal(expected.PdfX - 20f, r.PdfX, 0.01f);
                Assert.Equal(expected.PdfY - 30f, r.PdfY, 0.01f);
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            if (File.Exists(uncroppedPath)) File.Delete(uncroppedPath);
        }
    }

    private static void CreateCroppedPdf(string path, iText.Kernel.Geom.Rectangle cropBox)
    {
        using var writer = new PdfWriter(path);
        using var pdfDoc = new PdfDocument(writer);
        var page = pdfDoc.AddNewPage(new iText.Kernel.Geom.PageSize(600, 800));
        var font = PdfFontFactory.CreateFont(
            @"C:\Windows\Fonts\arial.ttf",
            PdfEncodings.IDENTITY_H,
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
        new iText.Kernel.Pdf.Canvas.PdfCanvas(page)
            .BeginText().SetFontAndSize(font, 12).MoveText(100, 200).ShowText("đi kinh hành").EndText();
        page.SetCropBox(cropBox);
    }

    private static void CreateTwoPagePdf(string path, string page1Text, string page2Text)
    {
        using var writer = new PdfWriter(path);
        using var pdfDoc = new PdfDocument(writer);
        using var doc = new Document(pdfDoc);
        var font = PdfFontFactory.CreateFont(
            @"C:\Windows\Fonts\arial.ttf",
            PdfEncodings.IDENTITY_H,
            PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
        doc.Add(new Paragraph(page1Text).SetFont(font));
        doc.Add(new iText.Layout.Element.AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
        doc.Add(new Paragraph(page2Text).SetFont(font));
    }

    public void Dispose()
    {
        if (File.Exists(_tempPdf)) File.Delete(_tempPdf);
        GC.SuppressFinalize(this);
    }
}
