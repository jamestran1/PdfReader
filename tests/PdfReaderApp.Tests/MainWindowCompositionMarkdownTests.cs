using System.IO;
using System.Xml.Linq;

namespace PdfReaderApp.Tests;

public sealed class MainWindowCompositionMarkdownTests
{
    [Fact]
    public void CompositionReadMode_RendersContentWithSharedMarkdownPresenter()
    {
        XNamespace controls = "clr-namespace:PdfReaderApp.Controls";
        string path = Path.Combine(TestPaths.RepoRoot(), "src", "PdfReaderApp", "Controls", "CompositionView.xaml");
        var xaml = XDocument.Load(path, LoadOptions.None);

        var presenter = Assert.Single(xaml.Descendants(controls + "MarkdownPresenter"));

        Assert.Equal("{Binding MarkdownBlocks}", (string?)presenter.Attribute("Blocks"));
        Assert.Contains(
            "Compositions.Sources.OpenCitationCommand",
            presenter.Element(controls + "MarkdownPresenter.RenderContext")!
                .Element(controls + "MarkdownRenderContext")!
                .Attribute("CitationCommand")!.Value,
            StringComparison.Ordinal);
        Assert.Equal(
            "True",
            (string?)presenter.Element(controls + "MarkdownPresenter.RenderContext")!
                .Element(controls + "MarkdownRenderContext")!
                .Attribute("RenderCitationsAsChips"));
    }

}
