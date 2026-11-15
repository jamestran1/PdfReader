using System.IO;

namespace PdfReaderApp.Tests;

public class PdfViewerGutterTests
{
    private static string XamlPath()
        => Path.Combine(TestPaths.RepoRoot(), "src", "PdfReaderApp", "Controls", "PdfViewerControl.xaml");

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
