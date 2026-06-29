using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PdfReaderApp.Tests;

public class RightPanelStyleTests
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

    private static string DictPath()
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", "RightPanel.xaml");

    [Fact]
    public void RightPanel_DefinesIconButtonStyle()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = XDocument.Load(DictPath()).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TriThu.RightPanel.IconButton", keys);
    }

    [Fact]
    public void RightPanel_UsesTokensNotHardcodedColors()
    {
        // #63 AC3: moi net ve qua {DynamicResource TriThu.Brush.*}, khong hardcode hex.
        var raw = File.ReadAllText(DictPath());
        Assert.DoesNotMatch(new Regex("=\"#[0-9A-Fa-f]{6,8}\""), raw);
    }
}
