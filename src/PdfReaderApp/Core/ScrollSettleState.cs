namespace PdfReaderApp.Core;

/// <summary>
/// Kết quả một tick ổn định hoá: có dừng timer không, và có phải cuộn lại tới offset đích không.
/// </summary>
/// <param name="ShouldStop">true khi timer phải dừng.</param>
/// <param name="ReassertOffset">Offset dọc cần cuộn lại tới; null nếu không phải cuộn.</param>
public readonly record struct ScrollSettleStep(bool ShouldStop, double? ReassertOffset);

/// <summary>
/// State machine thuần của việc cuộn tới trang đích sau khi nạp tài liệu. Extent ảo hoá lớn dần qua nhiều
/// đợt layout và có thể reset offset về 0, nên không thể cuộn một lần rồi gỡ guard: mỗi tick re-assert
/// offset đích, chỉ gỡ guard khi offset đã đứng yên tại đích vài tick liên tiếp hoặc hết trần thời gian.
/// </summary>
public sealed class ScrollSettleState
{
    /// <summary>Số tick đứng yên liên tiếp tại đích để coi là đã dính.</summary>
    public const int StableTicksRequired = 3;

    /// <summary>Trần số tick (khoảng 2,4 giây ở chu kỳ 30 ms) để khỏi kẹt guard.</summary>
    public const int MaxTicks = 80;

    private const double OffsetTolerance = 1;

    private int _stableTicks;
    private int _elapsedTicks;

    /// <summary>Trang (1-based) đang chờ cuộn tới; 0 = không có. Khi lớn hơn 0, không đồng bộ cuộn sang trang.</summary>
    public int PendingPage { get; private set; }

    /// <summary>true khi đang chờ cuộn tới trang đích.</summary>
    public bool IsPending => PendingPage > 0;

    /// <summary>Bật guard ngay (trước layout) mà chưa đặt lại bộ đếm tick.</summary>
    public void Arm(int page) => PendingPage = page;

    /// <summary>Bắt đầu ổn định hoá tới <paramref name="page"/>: bật guard và đặt lại bộ đếm tick.</summary>
    public void Begin(int page)
    {
        PendingPage = page;
        _stableTicks = 0;
        _elapsedTicks = 0;
    }

    /// <summary>Gỡ guard.</summary>
    public void Clear() => PendingPage = 0;

    /// <summary>
    /// Xử lý một tick của timer.
    /// </summary>
    /// <param name="hasDocument">Có tài liệu đang nạp không.</param>
    /// <param name="slotYOfPage">Trả về Y của slot theo chỉ số trang 0-based, hoặc null nếu slot chưa dựng.</param>
    /// <param name="scrollableHeight">Chiều cao cuộn được hiện tại.</param>
    /// <param name="verticalOffset">Offset dọc hiện tại.</param>
    public ScrollSettleStep Tick(bool hasDocument, Func<int, double?> slotYOfPage, double scrollableHeight, double verticalOffset)
    {
        if (!IsPending || !hasDocument) return new ScrollSettleStep(true, null);
        _elapsedTicks++;

        double? slotY = slotYOfPage(PendingPage - 1);
        if (slotY is null) return WaitForSlot();

        double target = Math.Min(slotY.Value, scrollableHeight);
        double? reassert = null;
        if (Math.Abs(verticalOffset - target) > OffsetTolerance)
        {
            reassert = target;
            _stableTicks = 0;
        }
        else
        {
            _stableTicks++;
        }

        if (_stableTicks >= StableTicksRequired || _elapsedTicks > MaxTicks)
        {
            Clear();
            return new ScrollSettleStep(true, reassert);
        }
        return new ScrollSettleStep(false, reassert);
    }

    private ScrollSettleStep WaitForSlot()
    {
        if (_elapsedTicks <= MaxTicks) return new ScrollSettleStep(false, null);
        Clear();
        return new ScrollSettleStep(true, null);
    }
}
