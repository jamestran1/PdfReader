using System.IO;
using iText.Kernel.Pdf;
using iText.Kernel.XMP;
using iText.Kernel.XMP.Options;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class PdfMetadataReaderTests : IDisposable
{
    private readonly string _dir;

    public PdfMetadataReaderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    private string MakePdf(string name, string? title, string? author)
    {
        string path = Path.Combine(_dir, name);
        using var writer = new PdfWriter(path);
        using var pdf = new PdfDocument(writer);
        if (title is not null) pdf.GetDocumentInfo().SetTitle(title);
        if (author is not null) pdf.GetDocumentInfo().SetAuthor(author);
        pdf.AddNewPage();
        return path;
    }

    [Fact]
    public void Read_WhenTitleAndAuthorSet_ReturnsThem()
    {
        var metadata = PdfMetadataReader.Read(MakePdf("a.pdf", "Lập trình WPF", "Trần Bình"));

        Assert.Equal("Lập trình WPF", metadata.Title);
        Assert.Equal("Trần Bình", metadata.Author);
    }

    [Fact]
    public void Read_WhenTitleAndAuthorAbsent_ReturnsNulls()
    {
        var metadata = PdfMetadataReader.Read(MakePdf("b.pdf", title: null, author: null));

        Assert.Null(metadata.Title);
        Assert.Null(metadata.Author);
        Assert.Null(metadata.Publisher);
    }

    [Fact]
    public void Read_WhenTitleIsWhitespace_ReturnsNull()
    {
        var metadata = PdfMetadataReader.Read(MakePdf("c.pdf", title: "   ", author: null));

        Assert.Null(metadata.Title);
    }

    [Fact]
    public void Read_WhenXmpDublinCorePublisherSet_ReturnsPublisher()
    {
        string path = Path.Combine(_dir, "d.pdf");
        using (var writer = new PdfWriter(path))
        using (var pdf = new PdfDocument(writer))
        {
            XMPMeta xmp = XMPMetaFactory.Create();
            xmp.AppendArrayItem(XMPConst.NS_DC, "publisher",
                new PropertyOptions(PropertyOptions.ARRAY), "NXB Kim Đồng", null);
            pdf.SetXmpMetadata(xmp);
            pdf.AddNewPage();
        }

        var metadata = PdfMetadataReader.Read(path);

        Assert.Equal("NXB Kim Đồng", metadata.Publisher);
    }

    [Fact]
    public void Read_WhenFileIsNotAValidPdf_ReturnsAllNull()
    {
        string path = Path.Combine(_dir, "broken.pdf");
        File.WriteAllText(path, "this is not a pdf");

        var metadata = PdfMetadataReader.Read(path);

        Assert.Null(metadata.Title);
        Assert.Null(metadata.Author);
        Assert.Null(metadata.Publisher);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
