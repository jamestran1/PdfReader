using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PdfReaderApp.Tests;

public class LibraryCardStyleTests
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
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", "LibraryCard.xaml");

    private static string MainWindowPath()
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml");

    [Fact]
    public void LibraryCard_DefinesCardHeaderAndOverlayStyles()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = XDocument.Load(DictPath()).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TriThu.LibraryCard", keys);
        Assert.Contains("TriThu.LibraryCard.Delete", keys);
        Assert.Contains("TriThu.LibraryCard.PagePill", keys);
        Assert.Contains("TriThu.Library.SearchBox", keys);
    }

    [Fact]
    public void LibraryCard_DefinesPublisherChipStyle()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = XDocument.Load(DictPath()).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TriThu.LibraryCard.PublisherChip", keys);
    }

    [Fact]
    public void LibraryCard_BindsAuthorAndPublisher()
    {
        var xaml = File.ReadAllText(MainWindowPath());

        Assert.Contains("{StaticResource TriThu.LibraryCard.PublisherChip}", xaml);
        Assert.Contains("Text=\"{Binding Publisher}\"", xaml);
        Assert.Contains("Text=\"{Binding Author}\"", xaml);
    }

    [Fact]
    public void LibraryCard_DoesNotHardcodeBrandColors()
    {
        // Cho phép scrim đen trong suốt (#AARRGGBB với RGB=000000) đè lên bìa tuỳ ý;
        // cấm hex màu thương hiệu (mọi hex có thành phần RGB khác 000000).
        var raw = File.ReadAllText(DictPath());
        var hexColors = Regex.Matches(raw, "\"#([0-9A-Fa-f]{6,8})\"");
        foreach (Match match in hexColors)
        {
            string hex = match.Groups[1].Value;
            string rgb = hex.Length == 8 ? hex.Substring(2) : hex;
            Assert.Equal("000000", rgb.ToUpperInvariant());
        }
    }
}
