using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

/// <summary>
/// Ghim quyết định "cần cuộn sau nạp": Đoạn nguồn giữa trang 1 phải được cuộn tới (#245), mở thường vẫn ở đầu trang 1.
/// </summary>
public class ScrollAfterLoadPolicyTests
{
    [Fact]
    public void NeedsScroll_PageOneWithPassageBelowTop_ReturnsTrue()
    {
        Assert.True(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: true, currentPage: 1, targetOffset: 240));
    }

    [Fact]
    public void NeedsScroll_PageOneAtTop_ReturnsFalse()
    {
        Assert.False(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: true, currentPage: 1, targetOffset: 0));
    }

    [Fact]
    public void NeedsScroll_PageOneCenteredInRowWithoutPassage_ReturnsFalse()
    {
        // Chế độ hai trang không bìa căn giữa trang theo hàng nên slot.Y của trang 1 có thể lớn hơn 0.
        Assert.False(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: true, currentPage: 1, targetOffset: 12, pageTop: 12));
    }

    [Fact]
    public void NeedsScroll_PageOneCenteredInRowWithPassage_ReturnsTrue()
    {
        Assert.True(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: true, currentPage: 1, targetOffset: 252, pageTop: 12));
    }

    [Fact]
    public void NeedsScroll_PageOneWithoutSlotYet_ReturnsFalse()
    {
        Assert.False(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: true, currentPage: 1, targetOffset: null));
    }

    [Fact]
    public void NeedsScroll_LaterPageWithoutSlotYet_ReturnsTrue()
    {
        Assert.True(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: true, currentPage: 5, targetOffset: null));
    }

    [Fact]
    public void NeedsScroll_NotContinuous_ReturnsFalse()
    {
        Assert.False(ScrollAfterLoadPolicy.NeedsScroll(isContinuous: false, currentPage: 3, targetOffset: 500));
    }
}
