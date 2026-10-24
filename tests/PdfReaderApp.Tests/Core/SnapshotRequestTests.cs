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
    public void Request_FallsBackToDefaultDelay_WhenDelayIsNotAPositiveNumber()
    {
        Assert.Equal(TimeSpan.FromSeconds(3), SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "main.png"), ("TRITHU_SCREENSHOT_DELAY", "abc")))!.Delay);
        Assert.Equal(TimeSpan.FromSeconds(3), SnapshotRequest.FromEnvironment(Env(
            ("TRITHU_SCREENSHOT", "main.png"), ("TRITHU_SCREENSHOT_DELAY", "-2")))!.Delay);
    }
}
