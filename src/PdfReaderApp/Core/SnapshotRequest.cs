using System.Globalization;

namespace PdfReaderApp.Core;

/// <summary>
/// Yêu cầu tự chụp cửa sổ chính (công cụ dev, dùng cho ảnh đính PR): đặt <c>TRITHU_SCREENSHOT</c> là đường dẫn
/// file .png, tuỳ chọn <c>TRITHU_SCREENSHOT_DELAY</c> là số giây chờ sau khi cửa sổ hiện ra.
/// </summary>
public sealed record SnapshotRequest(string Path, TimeSpan Delay)
{
    public const string PathVariable = "TRITHU_SCREENSHOT";
    public const string DelayVariable = "TRITHU_SCREENSHOT_DELAY";

    private static readonly TimeSpan DefaultDelay = TimeSpan.FromSeconds(3);

    /// <summary>Null khi không có yêu cầu chụp.</summary>
    public static SnapshotRequest? FromEnvironment(Func<string, string?> getVariable)
    {
        var path = getVariable(PathVariable);
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var delay = double.TryParse(getVariable(DelayVariable), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) && seconds > 0
            ? TimeSpan.FromSeconds(seconds)
            : DefaultDelay;
        return new SnapshotRequest(path.Trim(), delay);
    }
}
