using PdfReaderApp.Core;

namespace PdfReaderApp.Tests;

public sealed class TabDropPositionTests
{
    // Ba tab rộng 100, 60, 80: mép phải lần lượt ở 100, 160, 240.
    private static readonly double[] Widths = [100, 60, 80];

    [Theory]
    [InlineData(-30, 0)]    // trước tab đầu
    [InlineData(10, 0)]     // nửa trái tab 0
    [InlineData(60, 1)]     // nửa phải tab 0: khe sau tab 0
    [InlineData(120, 1)]    // nửa trái tab 1
    [InlineData(140, 2)]    // nửa phải tab 1
    [InlineData(199, 2)]    // nửa trái tab 2
    [InlineData(201, 3)]    // nửa phải tab 2
    [InlineData(900, 3)]    // quá tab cuối
    public void SlotAt_PicksTheGapNearestToThePointer(double pointerX, int expectedSlot)
        => Assert.Equal(expectedSlot, TabDropPosition.SlotAt(Widths, pointerX));

    [Fact]
    public void SlotAt_WithNoTabs_IsSlotZero()
        => Assert.Equal(0, TabDropPosition.SlotAt([], 50));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 100)]
    [InlineData(2, 160)]
    [InlineData(3, 240)]
    [InlineData(9, 240)]    // khe vượt số tab kẹp về mép phải tab cuối
    public void SlotOffset_IsTheSumOfTheWidthsBeforeTheGap(int slot, double expectedOffset)
        => Assert.Equal(expectedOffset, TabDropPosition.SlotOffset(Widths, slot));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, 1, 0)]
    [InlineData(0, 3, 2)]   // kéo tab đầu thả sau tab cuối
    [InlineData(2, 0, 0)]   // kéo tab cuối thả trước tab đầu
    [InlineData(2, 1, 1)]
    [InlineData(2, 3, 2)]
    [InlineData(1, 0, 0)]
    [InlineData(1, 3, 2)]
    public void TargetIndex_IsTheFinalIndexAfterRemovingTheDraggedTab(int dragIndex, int slot, int expected)
        => Assert.Equal(expected, TabDropPosition.TargetIndex(dragIndex, slot));

    [Theory]
    [InlineData(1, 1, true)]    // khe ngay trước chính nó
    [InlineData(1, 2, true)]    // khe ngay sau chính nó
    [InlineData(1, 0, false)]
    [InlineData(1, 3, false)]
    public void IsNoOp_WhenDroppedBesideTheDraggedTab(int dragIndex, int slot, bool expected)
        => Assert.Equal(expected, TabDropPosition.IsNoOp(dragIndex, slot));
}
