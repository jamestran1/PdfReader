using System.IO;
using System.Xml.Linq;

namespace PdfReaderApp.Tests;

public sealed class MainWindowNotesMarkdownTests
{
    [Fact]
    public void NotesReadMode_RendersContentWithMarkdownPresenterAndKeepsQuoteRaw()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace controls = "clr-namespace:PdfReaderApp.Controls";
        string path = Path.Combine(RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml");
        var xaml = XDocument.Load(path, LoadOptions.None);

        var notePresenters = xaml.Descendants(controls + "MarkdownPresenter")
            .Where(element => (string?)element.Attribute("Blocks") == "{Binding MarkdownBlocks}")
            .ToList();
        var rawQuote = xaml.Descendants(presentation + "TextBlock")
            .Single(element => (string?)element.Attribute("Text") == "{Binding Quote}");

        Assert.Equal(2, xaml.Descendants(controls + "MarkdownPresenter").Count());
        Assert.Contains(notePresenters, element => (string?)element.Attribute("FallbackInlineSpans") == "{Binding MarkdownSpans}");
        Assert.Equal("{Binding Quote}", (string?)rawQuote.Attribute("Text"));
    }

    [Fact]
    public void NotesReadMode_ExposesRawMarkdownCopyCommand()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        string path = Path.Combine(RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml");
        var xaml = XDocument.Load(path, LoadOptions.None);

        var copyButton = Assert.Single(xaml.Descendants(presentation + "Button"), element =>
            (string?)element.Attribute("ToolTip") == "Sao chép Markdown"
            && element.Attribute("Command")!.Value.Contains("Notes.CopyCommand"));

        Assert.Equal("28", (string?)copyButton.Attribute("Width"));
        Assert.Equal("28", (string?)copyButton.Attribute("Height"));
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Không tìm thấy gốc repo.");
    }
}
