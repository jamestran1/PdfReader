using System.IO;
using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core.Commands;

namespace PdfReaderApp.Tests.Controls;

public class PdfDocumentSessionTests
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
    public void Open_SnapshotSample_ReportsPageCountAndSizes()
    {
        using var session = PdfDocumentSession.Open(SamplePath());

        Assert.Equal(3, session.PageCount);
        Assert.Equal(595.0, session.GetPageSize(0).Width, 0);
        Assert.Equal(842.0, session.GetPageSize(0).Height, 0);
        Assert.Equal(3, session.GetPageSizes().Count);
    }

    [Fact]
    public void Open_InvalidFile_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.pdf");
        File.WriteAllText(path, "không phải PDF");
        try
        {
            Assert.ThrowsAny<Exception>(() => PdfDocumentSession.Open(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var session = PdfDocumentSession.Open(SamplePath());

        session.Dispose();
        session.Dispose();
    }

    [Fact]
    public void Dispose_ReleasesBackingStream()
    {
        var session = PdfDocumentSession.Open(SamplePath());
        var stream = session.Stream;

        session.Dispose();

        Assert.False(stream.CanRead);
    }

    [Fact]
    public void ResetEditState_ClearsUndoStack()
    {
        using var session = PdfDocumentSession.Open(SamplePath());
        session.UndoStack.Push(new EditTextCommand(0, 0, "a", "b"));

        session.ResetEditState();

        Assert.Empty(session.UndoStack);
    }
}
