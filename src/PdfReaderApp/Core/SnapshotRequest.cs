using System.Globalization;

namespace PdfReaderApp.Core;

/// <summary>
/// Yêu cầu tự chụp cửa sổ chính (công cụ dev, dùng cho ảnh đính PR): đặt <c>TRITHU_SCREENSHOT</c> là đường dẫn
/// file .png, tuỳ chọn <c>TRITHU_SCREENSHOT_DELAY</c> là số giây chờ sau khi cửa sổ hiện ra.
/// Cảnh chụp (tuỳ chọn): <c>TRITHU_SCREENSHOT_OPEN</c> (file PDF để mở), <c>TRITHU_SCREENSHOT_PAGE</c>,
/// <c>TRITHU_SCREENSHOT_ZOOM</c>, <c>TRITHU_SCREENSHOT_PANEL</c> (<c>chat</c> hoặc <c>notes</c>),
/// <c>TRITHU_SCREENSHOT_DIALOG</c> (<c>settings</c>: chụp hộp thoại thay cho cửa sổ chính).
/// </summary>
public sealed record SnapshotRequest(string Path, TimeSpan Delay)
{
    public const string PathVariable = "TRITHU_SCREENSHOT";
    public const string DelayVariable = "TRITHU_SCREENSHOT_DELAY";
    public const string OpenVariable = "TRITHU_SCREENSHOT_OPEN";
    public const string PageVariable = "TRITHU_SCREENSHOT_PAGE";
    public const string ZoomVariable = "TRITHU_SCREENSHOT_ZOOM";
    public const string PanelVariable = "TRITHU_SCREENSHOT_PANEL";
    public const string DialogVariable = "TRITHU_SCREENSHOT_DIALOG";

    private const double MaxDelaySeconds = 600;
    private const double MaxZoom = 10;
    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(3);

    /// <summary>File PDF cần import và mở trước khi chụp.</summary>
    public string? OpenPath { get; init; }

    /// <summary>Trang (từ 1) cần chuyển tới.</summary>
    public int? Page { get; init; }

    public double? Zoom { get; init; }

    /// <summary>Tab của panel phải: 0 = Chat, 1 = Ghi chú.</summary>
    public int? RightTabIndex { get; init; }

    /// <summary>Hộp thoại cần mở và chụp thay cho cửa sổ chính.</summary>
    public SnapshotDialog? Dialog { get; init; }

    /// <summary>Null khi không có yêu cầu chụp.</summary>
    public static SnapshotRequest? FromEnvironment(Func<string, string?> getVariable)
    {
        var path = getVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var delay = TryParsePositive(getVariable(DelayVariable), MaxDelaySeconds, out var seconds) ? TimeSpan.FromSeconds(seconds) : DefaultDelay;
        var openPath = getVariable(OpenVariable);
        return new SnapshotRequest(path.Trim(), delay)
        {
            OpenPath = string.IsNullOrWhiteSpace(openPath) ? null : openPath.Trim(),
            Page = int.TryParse(getVariable(PageVariable), NumberStyles.Integer, CultureInfo.InvariantCulture, out var page) && page > 0 ? page : null,
            Zoom = TryParsePositive(getVariable(ZoomVariable), MaxZoom, out var zoom) ? zoom : null,
            RightTabIndex = ParsePanel(getVariable(PanelVariable)),
            Dialog = ParseDialog(getVariable(DialogVariable)),
        };
    }

    // Cận trên: số quá lớn hoặc Infinity làm TimeSpan.FromSeconds ném OverflowException ngay lúc khởi động.
    private static bool TryParsePositive(string? text, double max, out double value)
        => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value > 0 && value <= max;

    private static int? ParsePanel(string? panel) => panel?.Trim().ToUpperInvariant() switch
    {
        "CHAT" => 0,
        "NOTES" => 1,
        _ => null,
    };

    private static SnapshotDialog? ParseDialog(string? dialog) => dialog?.Trim().ToUpperInvariant() switch
    {
        "SETTINGS" => SnapshotDialog.Settings,
        _ => null,
    };
}
