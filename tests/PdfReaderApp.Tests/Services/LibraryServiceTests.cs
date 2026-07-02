using System;
using System.IO;
using System.Linq;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class LibraryServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _libDir;
    private readonly string _thumbDir;
    private readonly LibraryService _svc;

    public LibraryServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _libDir = Path.Combine(_dir, "library");
        _thumbDir = Path.Combine(_libDir, "thumbs");
        Directory.CreateDirectory(_dir);
        var store = new SqliteLibraryStore(Path.Combine(_dir, "library.db"));
        store.EnsureSchema();
        _svc = new LibraryService(store, _libDir, _thumbDir, new PdfReaderApp.Services.DocnetPdfRenderService());
    }

    private string MakePdf(string name, string? title = null, string? author = null)
    {
        string path = Path.Combine(_dir, name);
        using var writer = new PdfWriter(path);
        using var pdf = new PdfDocument(writer);
        if (title is not null) pdf.GetDocumentInfo().SetTitle(title);
        if (author is not null) pdf.GetDocumentInfo().SetAuthor(author);
        using var doc = new Document(pdf);
        var font = PdfFontFactory.CreateFont(@"C:\Windows\Fonts\arial.ttf",
            PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
        doc.Add(new Paragraph("Trang một").SetFont(font));
        doc.Add(new AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
        doc.Add(new Paragraph("Trang hai").SetFont(font));
        return path;
    }

    [Fact]
    public void Import_CopiesFile_AddsRow_WithTitleAndPageCount()
    {
        var item = _svc.Import(MakePdf("sach.pdf"), nowUnix: 1000);

        Assert.Equal("sach.pdf", item.Title);
        Assert.Equal(2, item.PageCount);
        Assert.True(File.Exists(item.StoredPath), "stored copy should exist");
        Assert.StartsWith(_libDir, item.StoredPath);
        Assert.Single(_svc.GetAll());
    }

    [Fact]
    public void Import_SameContentTwice_DoesNotDuplicate()
    {
        string p = MakePdf("a.pdf");
        var first = _svc.Import(p, 1000);
        var again = _svc.Import(p, 2000);

        Assert.Equal(first.DocumentId, again.DocumentId);
        Assert.Single(_svc.GetAll());
        Assert.Equal(2000, _svc.GetAll()[0].LastOpenedAtUnix); // touched, not re-imported
    }

    [Fact]
    public void Remove_DeletesRowAndStoredFile()
    {
        var item = _svc.Import(MakePdf("b.pdf"), 1000);
        _svc.Remove(item);

        Assert.Empty(_svc.GetAll());
        Assert.False(File.Exists(item.StoredPath), "stored copy should be deleted");
    }

    [Fact]
    public void Import_WhenPdfHasTitleAndAuthor_UsesPdfTitleAndStoresAuthor()
    {
        var item = _svc.Import(MakePdf("filename.pdf", title: "Nhan đề thật", author: "Lê Cường"), nowUnix: 1000);

        Assert.Equal("Nhan đề thật", item.Title);
        Assert.Equal("Lê Cường", item.Author);
    }

    [Fact]
    public void Import_WhenPdfHasNoTitle_FallsBackToFileName()
    {
        var item = _svc.Import(MakePdf("khong-tieu-de.pdf"), nowUnix: 1000);

        Assert.Equal("khong-tieu-de.pdf", item.Title);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
