using System;
using System.IO;
using Xunit;

namespace PdfReaderApp.Tests;

public class SegmentedControlTrackTests
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

    private static string MainWindowPath()
        => Path.Combine(RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml");

    [Fact]
    public void SegmentedControl_DoesNotContainSegMaskElement()
    {
        // Regression: SegMask was a white-background Border that caused the track
        // to show white in dark mode. The fix removes it entirely.
        var xaml = File.ReadAllText(MainWindowPath());
        Assert.DoesNotContain("x:Name=\"SegMask\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentedControl_DoesNotContainHardcodedWhiteBackground()
    {
        // Regression: Background="White" on the SegMask Border must be gone.
        var xaml = File.ReadAllText(MainWindowPath());
        Assert.DoesNotContain("Background=\"White\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SegmentedControl_StackPanelNamedSegStackWithOpacityMask()
    {
        // The replacement uses x:Name="SegStack" with an inline VisualBrush
        // that binds Width/Height to ElementName=SegStack for corner-rounding.
        var xaml = File.ReadAllText(MainWindowPath());
        Assert.Contains("ElementName=SegStack", xaml, StringComparison.Ordinal);
    }
}
