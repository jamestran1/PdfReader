using PdfReaderApp.Controls.Viewer;

namespace PdfReaderApp.Tests.Controls;

public class ViewerScrollOnLoadTests
{
    // Đọc lẻ: đóng rồi mở lại cùng tệp (hoặc mở tệp khác) không được kế thừa ScrollNorm cục bộ còn sót.
    [Theory]
    [InlineData(0.6)]
    [InlineData(1.0)]
    public void UnboundViewer_StaleScrollNorm_OpensAtTopOfPage(double stale)
        => Assert.Equal(0, ViewerScrollOnLoad.NormToApply(isScrollNormBound: false, scrollNorm: stale));

    [Fact]
    public void BoundViewer_AppliesSavedScrollNorm()
        => Assert.Equal(0.6, ViewerScrollOnLoad.NormToApply(isScrollNormBound: true, scrollNorm: 0.6));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void BoundViewer_CorruptScrollNorm_OpensAtTopOfPage(double corrupt)
        => Assert.Equal(0, ViewerScrollOnLoad.NormToApply(isScrollNormBound: true, scrollNorm: corrupt));
}
