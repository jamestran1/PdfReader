using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

/// <summary>#261: phép đổi offset cuộn của viewer thành ScrollNorm của tab và ngược lại.</summary>
public class ViewerScrollPositionTests
{
    [Theory]
    [InlineData(1000, 1000, 800, 0)]      // đầu slot
    [InlineData(1400, 1000, 800, 0.5)]    // giữa slot
    [InlineData(1800, 1000, 800, 1)]      // cuối slot
    [InlineData(900, 1000, 800, 0)]       // offset nằm trên slot (khe giữa hai trang) kẹp về 0
    [InlineData(2500, 1000, 800, 1)]      // offset dưới slot kẹp về 1
    [InlineData(1400, 1000, 0, 0)]        // slot suy biến
    public void ToScrollNorm_ClampsFractionOfSlotScrolledPast(double offset, double slotY, double slotHeight, double expected)
        => Assert.Equal(expected, ViewerScrollPosition.ToScrollNorm(offset, slotY, slotHeight), precision: 9);

    [Theory]
    [InlineData(1000, 800, 0, 1000)]
    [InlineData(1000, 800, 0.5, 1400)]
    [InlineData(1000, 800, 1, 1800)]
    [InlineData(1000, 800, -0.3, 1000)]   // giá trị lưu hỏng bị kẹp
    [InlineData(1000, 800, 7, 1800)]
    [InlineData(1000, 800, double.NaN, 1000)]
    public void ToOffset_ClampsScrollNormIntoSlot(double slotY, double slotHeight, double norm, double expected)
        => Assert.Equal(expected, ViewerScrollPosition.ToOffset(slotY, slotHeight, norm), precision: 9);

    // Chuẩn hoá theo chiều cao slot nên đổi zoom (slot to gấp đôi) vẫn về cùng điểm tương đối trong trang.
    [Fact]
    public void RoundTrip_AfterZoomChange_LandsAtSameRelativePointInPage()
    {
        double norm = ViewerScrollPosition.ToScrollNorm(offset: 1200, slotY: 1000, slotHeight: 800);

        double offsetAtDoubleZoom = ViewerScrollPosition.ToOffset(slotY: 2000, slotHeight: 1600, norm);

        Assert.Equal(2400, offsetAtDoubleZoom, precision: 9);
    }

    [Theory]
    [InlineData(true, 800, false, true, true)]
    [InlineData(false, 800, false, true, false)]   // viewer ẩn (tab không active / rời Workspace): không ghi đè ScrollNorm
    [InlineData(false, 0, false, true, false)]
    [InlineData(true, 0, false, true, false)]      // viewport suy biến
    [InlineData(true, 800, true, true, false)]     // đang chờ cuộn tới đích sau nạp: offset trung gian
    [InlineData(true, 800, false, false, false)]   // ngoài chế độ cuộn liên tục
    public void ShouldPersist_OnlyWhenVisibleSettledAndContinuous(
        bool isVisible, double viewportHeight, bool isSettlePending, bool isContinuous, bool expected)
        => Assert.Equal(expected, ViewerScrollPosition.ShouldPersist(isVisible, viewportHeight, isSettlePending, isContinuous));
}
