using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Core;

/// <summary>
/// Ghim số liệu toạ độ overlay của PdfViewerControl: rect tìm kiếm là gốc dưới-trái (phải lật Y),
/// selection và note là gốc trên-trái (chỉ nhân zoom rồi cộng offset trang). dpi cố định 72.
/// </summary>
public class OverlayGeometryTests
{
    private static readonly RectF PageRect = new(10, 20, 612, 800);
    private const float PageHeightPt = 800f;

    // Công thức cũ của DrawHighlights, giữ nguyên để chốt số liệu trước khi chuyển code.
    private static RectF OldSearchMatch(MatchRect m, float scale)
    {
        var mapper = new PdfCoordinateMapper(PageHeightPt, scale, 72);
        var (rx, ry) = mapper.PdfPointToRender(m.PdfX, m.PdfY + m.Height);
        return new RectF(PageRect.Left + rx, PageRect.Top + ry, m.Width * scale, m.Height * scale);
    }

    // Công thức cũ của DrawSelectionOverlay và DrawSavedHighlights.
    private static RectF OldTopOrigin(double x, double y, double w, double h, float scale)
        => new((float)(PageRect.Left + x * scale), (float)(PageRect.Top + y * scale), (float)(w * scale), (float)(h * scale));

    [Theory]
    [InlineData(1.5f, 118f, 1052f, 75f, 18f)]
    [InlineData(2f, 154f, 1396f, 100f, 24f)]
    public void SearchMatch_FlipsYFromBottomLeftOrigin(float scale, float left, float top, float width, float height)
    {
        var rect = OldSearchMatch(new MatchRect(72, 100, 50, 12), scale);

        Assert.Equal(new RectF(left, top, width, height), rect);
    }

    [Theory]
    [InlineData(1.5f, 55f, 80f, 150f, 15f)]
    [InlineData(2f, 70f, 100f, 200f, 20f)]
    public void TopOrigin_ScalesAndOffsetsWithoutFlipping(float scale, float left, float top, float width, float height)
    {
        var rect = OldTopOrigin(30, 40, 100, 10, scale);

        Assert.Equal(new RectF(left, top, width, height), rect);
    }
}
