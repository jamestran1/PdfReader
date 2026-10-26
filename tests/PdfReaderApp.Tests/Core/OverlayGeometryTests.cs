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

    [Theory]
    [InlineData(1.5f, 118f, 1052f, 75f, 18f)]
    [InlineData(2f, 154f, 1396f, 100f, 24f)]
    public void BottomOrigin_FlipsYFromBottomLeftOrigin(float scale, float left, float top, float width, float height)
    {
        var rect = OverlayGeometry.BottomOrigin(new MatchRect(72, 100, 50, 12), PageHeightPt, scale, PageRect);

        Assert.Equal(new RectF(left, top, width, height), rect);
    }

    [Fact]
    public void PassageScrollOffset_NoRects_StaysAtPageTop()
    {
        double offset = OverlayGeometry.PassageScrollOffset(1000, PageHeightPt, 2f, [], viewportHeight: 600);

        Assert.Equal(1000, offset);
    }

    [Fact]
    public void PassageScrollOffset_PassageLowOnPage_PutsItAThirdOfViewportBelowTop()
    {
        // Cạnh trên của hộp: 1000 + (800 - (100 + 12)) * 2 = 2376; trừ 600 / 3.
        double offset = OverlayGeometry.PassageScrollOffset(
            1000, PageHeightPt, 2f, [new MatchRect(72, 100, 50, 12)], viewportHeight: 600);

        Assert.Equal(2176, offset);
    }

    [Fact]
    public void PassageScrollOffset_PassageNearPageTop_KeepsPageTopInView()
    {
        double offset = OverlayGeometry.PassageScrollOffset(
            1000, PageHeightPt, 2f, [new MatchRect(72, 770, 50, 12)], viewportHeight: 600);

        Assert.Equal(1000, offset);
    }

    [Fact]
    public void PassageScrollOffset_SeveralRects_UsesTheTopmost()
    {
        MatchRect[] rects = [new MatchRect(72, 100, 50, 12), new MatchRect(72, 300, 50, 12), new MatchRect(72, 200, 50, 12)];

        double offset = OverlayGeometry.PassageScrollOffset(1000, PageHeightPt, 2f, rects, viewportHeight: 600);

        Assert.Equal(1776, offset);
    }

    [Theory]
    [InlineData(1.5f, 55f, 80f, 150f, 15f)]
    [InlineData(2f, 70f, 100f, 200f, 20f)]
    public void TopOrigin_ScalesAndOffsetsWithoutFlipping(float scale, float left, float top, float width, float height)
    {
        var rect = OverlayGeometry.TopOrigin(30, 40, 100, 10, scale, PageRect);

        Assert.Equal(new RectF(left, top, width, height), rect);
    }
}
