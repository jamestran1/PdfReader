using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class PdfCoordinateMapperTests
{
    // US Letter: 612 x 792 points. At dpi=72, scale=1.0: 1 point = 1 pixel exactly.
    private static PdfCoordinateMapper LetterAt72Dpi() => new(792f, 1.0f, 72);

    [Fact]
    public void PdfPointToRender_BottomLeft_MapsToBottomOfRenderSpace()
    {
        var mapper = LetterAt72Dpi();
        var (rx, ry) = mapper.PdfPointToRender(0f, 0f);
        Assert.Equal(0f, rx);
        Assert.Equal(792f, ry); // PDF y=0 (bottom) -> render y=pageHeight
    }

    [Fact]
    public void PdfPointToRender_TopLeft_MapsToTopOfRenderSpace()
    {
        var mapper = LetterAt72Dpi();
        var (rx, ry) = mapper.PdfPointToRender(0f, 792f);
        Assert.Equal(0f, rx);
        Assert.Equal(0f, ry); // PDF y=792 (top) -> render y=0
    }

    [Fact]
    public void PdfPointToRender_KnownPoint_CorrectPixelPosition()
    {
        var mapper = LetterAt72Dpi();
        var (rx, ry) = mapper.PdfPointToRender(100f, 300f);
        Assert.Equal(100f, rx);
        Assert.Equal(492f, ry); // (792 - 300) * 1.0 * 1.0 = 492
    }

    [Fact]
    public void PdfPointToRender_HighDpi_ScalesPixels()
    {
        var mapper = new PdfCoordinateMapper(792f, 1.0f, 144); // 144 dpi = 2x
        var (rx, ry) = mapper.PdfPointToRender(100f, 300f);
        Assert.Equal(200f, rx); // 100 * 2
        Assert.Equal(984f, ry); // (792 - 300) * 2 = 984
    }

    [Fact]
    public void PdfPointToRender_WithScale_ScalesOutput()
    {
        var mapper = new PdfCoordinateMapper(792f, 2.0f, 72); // 2x zoom
        var (rx, ry) = mapper.PdfPointToRender(100f, 300f);
        Assert.Equal(200f, rx); // 100 * 2.0
        Assert.Equal(984f, ry); // (792 - 300) * 2.0
    }

    [Fact]
    public void RenderPointToPdf_RoundTrip_ReturnsOriginalPoint()
    {
        var mapper = LetterAt72Dpi();
        var (px, py) = mapper.RenderPointToPdf(100f, 492f);
        Assert.Equal(100f, px);
        Assert.Equal(300f, py);
    }
}
