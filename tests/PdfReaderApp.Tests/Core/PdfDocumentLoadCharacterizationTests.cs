using System.IO;
using PdfiumViewer.Core;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Core;

// Ghim cách viewer nạp PDF (PdfDocument.Load từ MemoryStream + vùng ảnh) trước khi tách PdfDocumentSession.
public class PdfDocumentLoadCharacterizationTests
{
    private static string SamplePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return Path.Combine(directory.FullName, "scripts", "assets", "snapshot-sample.pdf");
    }

    [Fact]
    public void LoadFromMemoryStream_SnapshotSample_ReportsPageCountAndSizes()
    {
        using var stream = new MemoryStream(File.ReadAllBytes(SamplePath()));
        using var document = PdfDocument.Load(stream);

        Assert.Equal(3, document.PageCount);
        Assert.Equal(595.0, document.Pages[0].Size.Width, 0);
        Assert.Equal(842.0, document.Pages[0].Size.Height, 0);
    }

    [Fact]
    public void GetNormalizedImageRectsByPage_SnapshotSample_ReturnsRectsInsideUnitSquare()
    {
        var rects = PdfImageLocator.GetNormalizedImageRectsByPage(SamplePath());

        foreach (var rect in rects.Values.SelectMany(r => r))
        {
            Assert.InRange(rect.Left, 0, 1);
            Assert.InRange(rect.Top, 0, 1);
        }
    }
}
