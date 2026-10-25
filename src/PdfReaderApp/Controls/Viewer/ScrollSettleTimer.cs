using System.Windows.Controls;
using System.Windows.Threading;
using PdfReaderApp.Core;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Gắn <see cref="ScrollSettleState"/> với <see cref="DispatcherTimer"/>: sau nạp tài liệu, extent ảo hoá lớn dần
/// qua nhiều đợt layout và có thể reset offset về 0, nên timer re-assert cuộn tới trang đích mỗi tick,
/// chỉ dừng khi offset đã đứng yên tại đích hoặc hết trần thời gian. Control chỉ gọi Arm/Begin/Cancel.
/// </summary>
internal sealed class ScrollSettleTimer
{
    private static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(30);

    private readonly ScrollSettleState _state = new();
    private readonly Func<bool> _hasDocument;
    private readonly Func<int, double?> _slotYOfPage;
    private readonly Func<ScrollViewport> _readViewport;
    private readonly Action<double> _scrollToOffset;
    private DispatcherTimer? _timer;

    public ScrollSettleTimer(ScrollViewer scrollViewer, Func<bool> hasDocument, Func<int, double?> slotYOfPage)
        : this(
            () => new ScrollViewport(scrollViewer.ScrollableHeight, scrollViewer.VerticalOffset),
            scrollViewer.ScrollToVerticalOffset,
            hasDocument,
            slotYOfPage)
    {
    }

    internal ScrollSettleTimer(
        Func<ScrollViewport> readViewport,
        Action<double> scrollToOffset,
        Func<bool> hasDocument,
        Func<int, double?> slotYOfPage)
    {
        _readViewport = readViewport;
        _scrollToOffset = scrollToOffset;
        _hasDocument = hasDocument;
        _slotYOfPage = slotYOfPage;
    }

    /// <summary>true khi đang chờ cuộn tới trang đích (không đồng bộ cuộn sang trang).</summary>
    public bool IsPending => _state.IsPending;

    /// <summary>Bật guard ngay (trước layout) mà chưa chạy timer; 0 = không chờ.</summary>
    public void Arm(int page) => _state.Arm(page);

    /// <summary>Bật guard tới <paramref name="page"/> và chạy timer ổn định hoá.</summary>
    public void Begin(int page)
    {
        _state.Begin(page);
        if (_timer == null)
        {
            _timer = new DispatcherTimer { Interval = TickInterval };
            _timer.Tick += (_, _) => Tick();
        }
        _timer.Start();
    }

    /// <summary>Gỡ guard và dừng timer.</summary>
    public void Cancel()
    {
        _state.Clear();
        _timer?.Stop();
    }

    internal void Tick()
    {
        // Slot chưa dựng (đang chờ layout) trả null; state machine bỏ cuộc sau trần tick để khỏi kẹt guard.
        ScrollSettleStep step = _state.Tick(_hasDocument(), _slotYOfPage, _readViewport());

        // Re-assert: layout có thể đã reset offset.
        if (step.ReassertOffset is { } offset) _scrollToOffset(offset);
        if (step.ShouldStop) _timer?.Stop();
    }
}
