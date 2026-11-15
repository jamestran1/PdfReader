using System.IO;
namespace PdfReaderApp.Tests;

public static class TestPaths
{
    public static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }
}
