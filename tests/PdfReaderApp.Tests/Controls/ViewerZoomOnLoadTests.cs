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
}
