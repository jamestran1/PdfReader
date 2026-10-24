using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

/// <summary>
/// Ghim hành vi ổn định cuộn sau khi nạp tài liệu (re-assert offset, đếm tick đứng yên, trần 80 tick).
/// </summary>
public class ScrollSettleStateTests
{
    private static readonly Func<int, double?> NoSlot = _ => null;

    private static Func<int, double?> SlotAt(int pageIndex, double y) => i => i == pageIndex ? y : null;

    [Fact]
    public void Tick_WhenNotPending_StopsWithoutReassert()
    {
        var state = new ScrollSettleState();

        var step = state.Tick(true, SlotAt(0, 100), 1000, 0);

        Assert.True(step.ShouldStop);
        Assert.Null(step.ReassertOffset);
    }

    [Fact]
    public void Tick_WithoutDocument_StopsButKeepsPending()
    {
        var state = new ScrollSettleState();
        state.Begin(3);

        var step = state.Tick(false, SlotAt(2, 100), 1000, 0);

        Assert.True(step.ShouldStop);
        Assert.Equal(3, state.PendingPage);
    }

    [Fact]
    public void Tick_OffsetAwayFromTarget_ReassertsAndKeepsRunning()
    {
        var state = new ScrollSettleState();
        state.Begin(2);

        var step = state.Tick(true, SlotAt(1, 500), 2000, 0);

        Assert.False(step.ShouldStop);
        Assert.Equal(500, step.ReassertOffset);
        Assert.True(state.IsPending);
    }

    [Fact]
    public void Tick_TargetClampedToScrollableHeight()
    {
        var state = new ScrollSettleState();
        state.Begin(2);

        var step = state.Tick(true, SlotAt(1, 5000), 800, 0);

        Assert.Equal(800, step.ReassertOffset);
    }

    [Fact]
    public void Tick_StableAtTargetForThreeTicks_ClearsGuardOnThirdTick()
    {
        var state = new ScrollSettleState();
        state.Begin(2);
        var slot = SlotAt(1, 500);

        var first = state.Tick(true, slot, 2000, 500);
        var second = state.Tick(true, slot, 2000, 500.5);
        var third = state.Tick(true, slot, 2000, 500);

        Assert.False(first.ShouldStop);
        Assert.False(second.ShouldStop);
        Assert.True(third.ShouldStop);
        Assert.Null(third.ReassertOffset);
        Assert.False(state.IsPending);
    }

    [Fact]
    public void Tick_OffsetDriftsAfterStableTicks_RestartsStableCount()
    {
        var state = new ScrollSettleState();
        state.Begin(2);
        var slot = SlotAt(1, 500);

        state.Tick(true, slot, 2000, 500);
        state.Tick(true, slot, 2000, 500);
        var drift = state.Tick(true, slot, 2000, 0);
        var afterDrift = state.Tick(true, slot, 2000, 500);

        Assert.False(drift.ShouldStop);
        Assert.Equal(500, drift.ReassertOffset);
        Assert.False(afterDrift.ShouldStop);
        Assert.True(state.IsPending);
    }

    [Fact]
    public void Tick_SlotNeverBuilt_GivesUpAfterMaxTicks()
    {
        var state = new ScrollSettleState();
        state.Begin(4);

        for (int i = 0; i < ScrollSettleState.MaxTicks; i++)
        {
            Assert.False(state.Tick(true, NoSlot, 1000, 0).ShouldStop);
        }
        var last = state.Tick(true, NoSlot, 1000, 0);

        Assert.True(last.ShouldStop);
        Assert.False(state.IsPending);
    }

    [Fact]
    public void Tick_NeverSettles_StopsAfterMaxTicksAndStillReasserts()
    {
        var state = new ScrollSettleState();
        state.Begin(2);
        var slot = SlotAt(1, 500);

        for (int i = 0; i < ScrollSettleState.MaxTicks; i++)
        {
            Assert.False(state.Tick(true, slot, 2000, 0).ShouldStop);
        }
        var last = state.Tick(true, slot, 2000, 0);

        Assert.True(last.ShouldStop);
        Assert.Equal(500, last.ReassertOffset);
        Assert.False(state.IsPending);
    }

    [Fact]
    public void Begin_ResetsCountersFromPreviousRun()
    {
        var state = new ScrollSettleState();
        state.Begin(2);
        var slot = SlotAt(1, 500);
        for (int i = 0; i < 70; i++) state.Tick(true, slot, 2000, 0);

        state.Begin(2);

        for (int i = 0; i < 70; i++)
        {
            Assert.False(state.Tick(true, slot, 2000, 0).ShouldStop);
        }
    }

    [Fact]
    public void ArmThenClear_TogglesPendingWithoutTicks()
    {
        var state = new ScrollSettleState();

        state.Arm(5);
        Assert.Equal(5, state.PendingPage);
        state.Clear();

        Assert.False(state.IsPending);
    }
}
