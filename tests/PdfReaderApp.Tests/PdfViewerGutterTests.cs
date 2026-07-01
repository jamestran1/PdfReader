using System;
using System.IO;
using Xunit;

namespace PdfReaderApp.Tests;

public class PdfViewerGutterTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Khong tim thay goc repo (PdfReaderApp.slnx).");
        return directory.FullName;
    }

    private static string XamlPath()
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Controls", "PdfViewerControl.xaml");

    [Fact]
    public void PdfViewerControl_GutterUsesSurfaceTonalLowToken()
    {
        var raw = File.ReadAllText(XamlPath());
        Assert.Contains("{DynamicResource TriThu.Brush.SurfaceTonalLow}", raw);
    }

    [Fact]
    public void PdfViewerControl_GutterDoesNotContainHardcodedColor()
    {
        var raw = File.ReadAllText(XamlPath());
        Assert.DoesNotContain("#525659", raw);
    }
}
