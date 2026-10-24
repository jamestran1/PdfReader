using PdfReaderApp.Core;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Core;

public class DarkPageImageOverlayTests
{
    [Fact]
    public void Map_ScalesSourceByBitmapAndDestinationByPageRect()
    {
        var image = new PdfImageLocator.NormalizedRect(0.25, 0.5, 0.5, 0.25);
        var (src, dst) = DarkPageImageOverlay.Map(image, 200, 400, new RectF(10, 20, 100, 300));

        Assert.Equal(new RectF(50, 200, 100, 100), src);
        Assert.Equal(new RectF(35, 170, 50, 75), dst);
    }
}
