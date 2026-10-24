using System.IO;

namespace PdfReaderApp.Core;

/// <summary>
/// Thư mục dữ liệu của app (settings, thư viện, index, ghi chú). Đặt <c>TRITHU_DATA_DIR</c> để chạy với
/// dữ liệu riêng, vd khi chụp ảnh cho PR, để không lộ thư viện thật.
/// </summary>
public static class AppDataDirectory
{
    public const string OverrideVariable = "TRITHU_DATA_DIR";
    private const string AppFolderName = "PdfReaderApp";

    /// <summary>Thư mục dữ liệu của tiến trình hiện tại; tạo nếu chưa có.</summary>
    public static string Current()
    {
        var dir = Resolve(
            Environment.GetEnvironmentVariable,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string Resolve(Func<string, string?> getVariable, string applicationDataFolder)
    {
        var overrideDir = getVariable(OverrideVariable);
        return string.IsNullOrWhiteSpace(overrideDir)
            ? Path.Combine(applicationDataFolder, AppFolderName)
            : overrideDir.Trim();
    }
}
