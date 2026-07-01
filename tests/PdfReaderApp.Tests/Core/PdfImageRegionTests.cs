using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class PdfImageRegionTests
{
    // Ảnh ở góc dưới-trái (PDF coords gốc dưới-trái): (0,0,50,50) trên trang 100x100
    // => Left=0, Top=0.5 (phần trên bị chiếm nửa trên), Width=0.5, Height=0.5
    [Fact]
    public void Normalize_BottomLeftQuadrant_ReturnsCorrectNormalized()
    {
        var (left, top, width, height) = PdfImageRegion.Normalize(0, 0, 50, 50, 100, 100);
        Assert.Equal(0.0,  left,   6);
        Assert.Equal(0.5,  top,    6);
        Assert.Equal(0.5,  width,  6);
        Assert.Equal(0.5,  height, 6);
    }

    // Ảnh ở góc trên-trái (PDF coords): (0,50,50,50) trên trang 100x100
    // => Left=0, Top=0 (phần trên cùng), Width=0.5, Height=0.5
    [Fact]
    public void Normalize_TopLeftQuadrant_ReturnsCorrectNormalized()
    {
        var (left, top, width, height) = PdfImageRegion.Normalize(0, 50, 50, 50, 100, 100);
        Assert.Equal(0.0, left,   6);
        Assert.Equal(0.0, top,    6);
        Assert.Equal(0.5, width,  6);
        Assert.Equal(0.5, height, 6);
    }

    // Ảnh chiếm toàn trang
    [Fact]
    public void Normalize_FullPage_ReturnsUnitRect()
    {
        var (left, top, width, height) = PdfImageRegion.Normalize(0, 0, 100, 100, 100, 100);
        Assert.Equal(0.0, left,   6);
        Assert.Equal(0.0, top,    6);
        Assert.Equal(1.0, width,  6);
        Assert.Equal(1.0, height, 6);
    }

    // Kích thước trang bằng 0 (degenerate) => trả về zeros
    [Fact]
    public void Normalize_DegeneratePageWidth_ReturnsZeros()
    {
        var (left, top, width, height) = PdfImageRegion.Normalize(0, 0, 10, 10, 0, 100);
        Assert.Equal(0.0, left);
        Assert.Equal(0.0, top);
        Assert.Equal(0.0, width);
        Assert.Equal(0.0, height);
    }

    // Kích thước chiều cao trang bằng 0
    [Fact]
    public void Normalize_DegeneratePageHeight_ReturnsZeros()
    {
        var (left, top, width, height) = PdfImageRegion.Normalize(0, 0, 10, 10, 100, 0);
        Assert.Equal(0.0, left);
        Assert.Equal(0.0, top);
        Assert.Equal(0.0, width);
        Assert.Equal(0.0, height);
    }

    // CTM gần trục toạ độ (b=c=0) => axis-aligned, giữ nguyên ảnh
    [Fact]
    public void IsAxisAligned_NoRotation_ReturnsTrue()
        => Assert.True(PdfImageRegion.IsAxisAligned(0, 0));

    // Hệ số chéo-phụ rất nhỏ (trong dung sai) => vẫn coi là axis-aligned
    [Fact]
    public void IsAxisAligned_WithinTolerance_ReturnsTrue()
        => Assert.True(PdfImageRegion.IsAxisAligned(0.005, -0.003));

    // b khác 0 đáng kể (ảnh xoay) => không axis-aligned
    [Fact]
    public void IsAxisAligned_RotatedB_ReturnsFalse()
        => Assert.False(PdfImageRegion.IsAxisAligned(1, 0));

    // c khác 0 đáng kể (ảnh nghiêng) => không axis-aligned
    [Fact]
    public void IsAxisAligned_SkewedC_ReturnsFalse()
        => Assert.False(PdfImageRegion.IsAxisAligned(0, 0.5));
}
