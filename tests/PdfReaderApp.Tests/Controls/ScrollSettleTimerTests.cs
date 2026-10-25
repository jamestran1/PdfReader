using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Controls;

public class ScrollSettleTimerTests
{
    private static ScrollSettleTimer Create(Func<ScrollViewport> viewport, List<double> scrolls, Func<bool>? hasDocument = null) =>
        new(viewport, scrolls.Add, hasDocument ?? (() => true), page => page * 100.0);

    [Fact]
    public void Tick_ReassertsTargetOffsetWhileOffsetIsOff()
    {
        var scrolls = new List<double>();
        var timer = Create(() => new ScrollViewport(10_000, 0), scrolls);
        timer.Begin(3);

        timer.Tick();

        Assert.Equal(new[] { 200.0 }, scrolls);
        Assert.True(timer.IsPending);
    }

    [Fact]
    public void Tick_ClearsGuardAfterOffsetStaysAtTarget()
    {
        var scrolls = new List<double>();
        var timer = Create(() => new ScrollViewport(10_000, 200), scrolls);
        timer.Begin(3);

        for (int i = 0; i < ScrollSettleState.StableTicksRequired; i++) timer.Tick();

        Assert.False(timer.IsPending);
        Assert.Empty(scrolls);
    }

    [Fact]
    public void Tick_WithoutDocumentDoesNotScroll()
    {
        var scrolls = new List<double>();
        var timer = Create(() => new ScrollViewport(10_000, 0), scrolls, () => false);
        timer.Begin(3);

        timer.Tick();

        Assert.Empty(scrolls);
    }

    [Fact]
    public void Cancel_ClearsGuardSetByArm()
    {
        var timer = Create(() => new ScrollViewport(10_000, 0), []);
        timer.Arm(4);
        Assert.True(timer.IsPending);

        timer.Cancel();

        Assert.False(timer.IsPending);
    }
}
