using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class CompositionScrollTests
{
    [Theory]
    [InlineData(0, 400, 0)]
    [InlineData(200, 400, 0.5)]
    [InlineData(400, 400, 1)]
    [InlineData(50, 0, 0)]      // nội dung ngắn hơn khung: không cuộn được
    [InlineData(900, 400, 1)]   // kẹp về [0..1]
    public void ToNormalized_MapsOffsetIntoZeroToOne(double offset, double scrollable, double expected)
        => Assert.Equal(expected, CompositionScroll.ToNormalized(offset, scrollable));

    [Theory]
    [InlineData(0.5, 400, 200)]
    [InlineData(1, 400, 400)]
    [InlineData(-1, 400, 0)]
    [InlineData(2, 400, 400)]
    [InlineData(0.5, 0, 0)]
    public void ToOffset_MapsNormalizedBackToOffset(double normalized, double scrollable, double expected)
        => Assert.Equal(expected, CompositionScroll.ToOffset(normalized, scrollable));
}
