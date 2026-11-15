using System.IO;

namespace PdfReaderApp.Tests;

public class SegmentedControlTrackTests
{
    private static string MainWindowPath()
        => Path.Combine(TestPaths.RepoRoot(), "src", "PdfReaderApp", "MainWindow.xaml");

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
