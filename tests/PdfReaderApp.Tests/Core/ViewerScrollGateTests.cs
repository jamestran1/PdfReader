using PdfReaderApp.Core;
using Xunit;

namespace PdfReaderApp.Tests.Core;

/// <summary>
/// Regression (bug 1): rời Workspace -> viewer của tab active bị ẩn -> ScrollViewer reset offset/viewport=0
/// và phát ScrollChanged; nếu đồng bộ scroll->trang lúc đó thì trang giữa khung = 1 bị ghi ngược vào
/// OpenTab.Page và lưu đè trang đã lưu về cover. PagesScrollViewer_ScrollChanged dùng
/// ViewerScrollGate.ShouldSyncPageFromScroll để chỉ đồng bộ khi viewer hiển thị thật.
/// </summary>
public class ViewerScrollGateTests
{
    [Theory]
    [InlineData(true, 800, true)]    // hiển thị + viewport thật -> đồng bộ trang
    [InlineData(false, 800, false)]  // tab không active / rời Workspace (ẩn) -> KHÔNG đồng bộ (chống về cover)
    [InlineData(false, 0, false)]    // ẩn + viewport rỗng -> không đồng bộ
    [InlineData(true, 0, false)]     // layout suy biến (viewport=0) -> không đồng bộ
    public void ShouldSyncPageFromScroll_OnlyWhenVisibleWithRealViewport(bool isVisible, double viewportHeight, bool expected)
        => Assert.Equal(expected, ViewerScrollGate.ShouldSyncPageFromScroll(isVisible, viewportHeight));
}
