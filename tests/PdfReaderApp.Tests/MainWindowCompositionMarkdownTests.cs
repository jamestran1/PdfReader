using System.IO;
using System.Xml.Linq;

namespace PdfReaderApp.Tests;

public sealed class MainWindowCompositionMarkdownTests
{
    [Fact]
    public void CompositionReadMode_RendersContentWithSharedMarkdownPresenter()
    {
        XNamespace controls = "clr-namespace:PdfReaderApp.Controls";
        string path = Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Controls", "CompositionView.xaml");
        var xaml = XDocument.Load(path, LoadOptions.None);

        var presenter = Assert.Single(xaml.Descendants(controls + "MarkdownPresenter"));

        Assert.Equal("{Binding MarkdownBlocks}", (string?)presenter.Attribute("Blocks"));
        Assert.Contains(
            "Compositions.Sources.OpenCitationCommand",
            presenter.Attribute("CitationCommand")!.Value,
            StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }
}
