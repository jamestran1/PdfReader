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
    public void HighlightMapping_MustMatchPageLayoutScale_Dpi72()
    {
        // The viewer lays out each page at pageSize * scale (1 PDF point = `scale` pixels).
        // The highlight overlay shares that canvas, so its mapper MUST produce the same
        // pixels-per-point: dpi=72. (dpi=96 over-scales by 96/72 and rects drift off the text.)
        const float pageH = 643f;
        const float scale = 2.6f;
        var mapper = new PdfCoordinateMapper(pageH, scale, 72);

        // Bottom of page (pdfY=0) must map to the bottom of the laid-out page rect (pageH*scale).
        var (_, ry) = mapper.PdfPointToRender(0f, 0f);
        Assert.Equal(pageH * scale, ry, 3);

        // A point at x maps to x*scale (matches pageRect width scaling).
        var (rx, _) = mapper.PdfPointToRender(100f, 0f);
        Assert.Equal(100f * scale, rx, 3);
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
