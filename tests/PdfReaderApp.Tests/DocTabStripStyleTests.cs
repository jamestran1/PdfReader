using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PdfReaderApp.Tests;

public class DocTabStripStyleTests
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
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", "DocTabStrip.xaml");

    [Fact]
    public void DocTabStrip_DefinesChipCloseAndAddStyles()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = XDocument.Load(DictPath()).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TriThu.DocTab.Chip", keys);
        Assert.Contains("TriThu.DocTab.Close", keys);
        Assert.Contains("TriThu.DocTab.Add", keys);
    }

    [Fact]
    public void DocTabStrip_UsesTokensNotHardcodedColors()
    {
        // #62 AC3: mọi nét vẽ phải qua {DynamicResource TriThu.Brush.*}, không hardcode hex.
        var raw = File.ReadAllText(DictPath());
        Assert.DoesNotMatch(new Regex("=\"#[0-9A-Fa-f]{6,8}\""), raw);
    }
}
