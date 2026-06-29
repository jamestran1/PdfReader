using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Xunit;

namespace PdfReaderApp.Tests;

public class WorkspaceCardStyleTests
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
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", "WorkspaceCard.xaml");

    [Fact]
    public void WorkspaceCard_DefinesCardDeleteAndCreateButtonStyles()
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var keys = XDocument.Load(DictPath()).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("TriThu.WorkspaceCard", keys);
        Assert.Contains("TriThu.WorkspaceCard.Delete", keys);
        Assert.Contains("TriThu.WorkspaceCard.CreateButton", keys);
    }

    [Fact]
    public void WorkspaceCard_UsesTokensNotHardcodedColors()
    {
        var raw = File.ReadAllText(DictPath());
        Assert.DoesNotMatch(new Regex("=\"#[0-9A-Fa-f]{6,8}\""), raw);
    }
}
