using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PdfReaderApp.Tests;

public class TriThuTokenParityTests
{
    // Các vai trò M3 mà MDT không mô hình hóa → TriThu sở hữu; #71 bổ sung cho đủ.
    private static readonly string[] NewGapTokenKeys =
    {
        "TriThu.Brush.TertiaryContainer",
        "TriThu.Brush.TertiaryContainer.On",
        "TriThu.Brush.ErrorContainer",
        "TriThu.Brush.ErrorContainer.On",
        "TriThu.Brush.SurfaceTonalLow",
        "TriThu.Brush.SurfaceTonal",
        "TriThu.Brush.SurfaceTonalHigh",
        "TriThu.Brush.InverseSurface.On",
        "TriThu.Brush.InversePrimary",
        "TriThu.Brush.State",
        "TriThu.Brush.Pdf",
        "TriThu.Brush.PdfText",
        "TriThu.Brush.PdfFaint",
        "TriThu.Brush.Surface",
        "TriThu.Brush.OnSurface",
    };

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "PdfReaderApp.slnx")))
            directory = directory.Parent;
        if (directory == null)
            throw new InvalidOperationException("Không tìm thấy gốc repo (PdfReaderApp.slnx).");
        return directory.FullName;
    }

    private static HashSet<string> BrushKeysOf(string themeFileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", themeFileName);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null && key.StartsWith("TriThu.Brush.", StringComparison.Ordinal))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    private static HashSet<string> AllTriThuKeysOf(string themeFileName)
    {
        var path = Path.Combine(RepoRoot(), "src", "PdfReaderApp", "Themes", themeFileName);
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        return XDocument.Load(path).Descendants()
            .Select(element => (string?)element.Attribute(x + "Key"))
            .Where(key => key is not null && key.StartsWith("TriThu.", StringComparison.Ordinal))
            .Select(key => key!)
            .ToHashSet(StringComparer.Ordinal);
    }

    [Fact]
    public void LightAndDarkTokens_ExposeTheSameTriThuKeys()
    {
        var lightKeys = AllTriThuKeysOf("TriThuTokens.xaml").OrderBy(k => k, StringComparer.Ordinal).ToList();
        var darkKeys  = AllTriThuKeysOf("TriThuTokens.Dark.xaml").OrderBy(k => k, StringComparer.Ordinal).ToList();

        var onlyInLight = lightKeys.Except(darkKeys, StringComparer.Ordinal).ToList();
        var onlyInDark  = darkKeys.Except(lightKeys, StringComparer.Ordinal).ToList();

        Assert.True(
            onlyInLight.Count == 0 && onlyInDark.Count == 0,
            $"Key chi trong light: [{string.Join(", ", onlyInLight)}] | Key chi trong dark: [{string.Join(", ", onlyInDark)}]");
    }

    [Fact]
    public void LightAndDarkTokens_ExposeTheSameBrushKeys()
    {
        Assert.Equal(
            BrushKeysOf("TriThuTokens.xaml").OrderBy(k => k, StringComparer.Ordinal),
            BrushKeysOf("TriThuTokens.Dark.xaml").OrderBy(k => k, StringComparer.Ordinal));
    }

    [Fact]
    public void BothThemes_DefineTheNewM3GapTokens()
    {
        var light = BrushKeysOf("TriThuTokens.xaml");
        var dark = BrushKeysOf("TriThuTokens.Dark.xaml");
        foreach (var key in NewGapTokenKeys)
        {
            Assert.Contains(key, light);
            Assert.Contains(key, dark);
        }
    }
}
