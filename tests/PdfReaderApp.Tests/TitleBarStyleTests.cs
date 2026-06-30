using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PdfReaderApp.Tests;

public class TitleBarStyleTests
{
    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return directory.FullName;
    }

    private static string DictPath()
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", "TitleBar.xaml");

    [Fact]
    public void TitleBar_DefinesCaptionAndCloseButtonStyles()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = XDocument.Load(DictPath()).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TriThu.TitleBar.CaptionButton", keys);
        Assert.Contains("TriThu.TitleBar.CloseButton", keys);
    }

    [Fact]
    public void TitleBar_UsesTokensNotHardcodedColors()
    {
        var raw = File.ReadAllText(DictPath());
        Assert.DoesNotMatch(new Regex("=\"#[0-9A-Fa-f]{6,8}\""), raw);
    }
}
