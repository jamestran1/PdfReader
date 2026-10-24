using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

/// <summary>
/// App tự chụp cửa sổ chính khi đặt TRITHU_SCREENSHOT (dùng cho ảnh đính PR của các đợt dọn code).
/// SnapshotRequest đọc biến môi trường; phần render WPF không test được nên tách riêng.
/// </summary>
public class SnapshotRequestTests
{
    private static Func<string, string?> Env(params (string Key, string Value)[] vars)
        => key => vars.FirstOrDefault(v => v.Key == key).Value;

    [Fact]
    public void NoRequest_WhenScreenshotVariableIsMissingOrBlank()
    {
        Assert.Null(SnapshotRequest.FromEnvironment(Env()));
        Assert.Null(SnapshotRequest.FromEnvironment(Env(("TRITHU_SCREENSHOT", "  "))));
    }

    [Fact]
    public void Request_UsesPathAndDefaultDelay()
    {
        var request = SnapshotRequest.FromEnvironment(Env(("TRITHU_SCREENSHOT", @"C:\shots\main.png")));

        Assert.NotNull(request);
        Assert.Equal(@"C:\shots\main.png", request.Path);
        Assert.Equal(TimeSpan.FromSeconds(3), request.Delay);
    }

    [Fact]
    public void Request_ReadsDelaySeconds()
    {
        var request = SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "main.png"),
            ("TRITHU_SCREENSHOT_DELAY", "7")));

        Assert.Equal(TimeSpan.FromSeconds(7), request!.Delay);
    }

    [Fact]
    public void Request_HasNoScene_ByDefault()
    {
        var request = SnapshotRequest.FromEnvironment(Env(("TRITHU_SCREENSHOT", "main.png")))!;

        Assert.Null(request.OpenPath);
        Assert.Null(request.Page);
        Assert.Null(request.Zoom);
        Assert.Null(request.RightTabIndex);
    }

    [Fact]
    public void Request_ReadsScene_OpenPageZoomAndPanel()
    {
        var request = SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "reader.png"),
            ("TRITHU_SCREENSHOT_OPEN", @"C:\samples\sample.pdf"),
            ("TRITHU_SCREENSHOT_PAGE", "2"),
            ("TRITHU_SCREENSHOT_ZOOM", "1.5"),
            ("TRITHU_SCREENSHOT_PANEL", "notes")))!;

        Assert.Equal(@"C:\samples\sample.pdf", request.OpenPath);
        Assert.Equal(2, request.Page);
        Assert.Equal(1.5, request.Zoom);
        Assert.Equal(1, request.RightTabIndex);
    }

    [Theory]
    [InlineData("chat", 0)]
    [InlineData("Notes", 1)]
    public void Request_MapsPanelNameToRightTabIndex(string panel, int expected)
    {
        var request = SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "x.png"), ("TRITHU_SCREENSHOT_PANEL", panel)))!;

        Assert.Equal(expected, request.RightTabIndex);
    }

    [Fact]
    public void Request_IgnoresInvalidSceneValues()
    {
        var request = SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "x.png"),
            ("TRITHU_SCREENSHOT_PAGE", "0"),
            ("TRITHU_SCREENSHOT_ZOOM", "abc"),
            ("TRITHU_SCREENSHOT_PANEL", "sidebar")))!;

        Assert.Null(request.Page);
        Assert.Null(request.Zoom);
        Assert.Null(request.RightTabIndex);
    }

    [Fact]
    public void Request_FallsBackToDefaultDelay_WhenDelayIsNotAPositiveNumber()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "main.png"), ("TRITHU_SCREENSHOT_DELAY", "abc")))!.Delay);
        Assert.Equal(TimeSpan.FromSeconds(3), SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "main.png"), ("TRITHU_SCREENSHOT_DELAY", "-2")))!.Delay);
    }

    [Theory]
    [InlineData("Infinity")]
    [InlineData("1e20")]
    [InlineData("601")]
    public void Request_FallsBackToDefaultDelay_WhenDelayIsTooLarge(string delay)
    {
        var request = SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "main.png"), ("TRITHU_SCREENSHOT_DELAY", delay)));

        Assert.Equal(TimeSpan.FromSeconds(3), request!.Delay);
    }
}
