using PdfReaderApp.Controls.Viewer;

namespace PdfReaderApp.Tests.Controls;

public class ViewerZoomOnLoadTests
{
    [Fact]
    public void FirstLoad_FitsToViewport()
    {
        var zoom = new ViewerZoomOnLoad();

        Assert.True(zoom.ShouldFitOnLoad("/a.pdf"));
    }

    [Fact]
    public void ReloadOfEvictedDocument_KeepsZoom()
    {
        var zoom = new ViewerZoomOnLoad();
        zoom.OnEvicted("/a.pdf");

        Assert.False(zoom.ShouldFitOnLoad("/a.pdf"));
    }

    // Viewer đọc lẻ nhả tài liệu khi vào Workspace; mở tài liệu khác sau đó vẫn phải fit, không kế thừa zoom cũ.
    [Fact]
    public void LoadOfOtherDocumentAfterEviction_FitsToViewport()
    {
        var zoom = new ViewerZoomOnLoad();
        zoom.OnEvicted("/a.pdf");

        Assert.True(zoom.ShouldFitOnLoad("/b.pdf"));
    }

    [Fact]
    public void SecondLoadAfterRehydrate_FitsAgain()
    {
        var zoom = new ViewerZoomOnLoad();
        zoom.OnEvicted("/a.pdf");
        zoom.ShouldFitOnLoad("/a.pdf");

        Assert.True(zoom.ShouldFitOnLoad("/a.pdf"));
    }

    // #263: tab khôi phục từ Open Set mang zoom đã lưu, lần nạp đầu không fit đè.
    [Fact]
    public void FirstLoadWithSavedZoom_KeepsZoom()
    {
        var zoom = new ViewerZoomOnLoad();

        Assert.False(zoom.ShouldFitOnLoad("/a.pdf", hasSavedZoom: true));
    }

    [Fact]
    public void FirstLoadWithoutSavedZoom_FitsToViewport()
    {
        var zoom = new ViewerZoomOnLoad();

        Assert.True(zoom.ShouldFitOnLoad("/a.pdf", hasSavedZoom: false));
    }

    // Tab khôi phục bị evict rồi nạp lại vẫn giữ zoom, cả khi dấu evict đã bị tiêu thụ.
    [Fact]
    public void SavedZoomTab_ReloadAfterEviction_KeepsZoom()
    {
        var zoom = new ViewerZoomOnLoad();
        zoom.ShouldFitOnLoad("/a.pdf", hasSavedZoom: true);
        zoom.OnEvicted("/a.pdf");

        Assert.False(zoom.ShouldFitOnLoad("/a.pdf", hasSavedZoom: true));
    }
}
