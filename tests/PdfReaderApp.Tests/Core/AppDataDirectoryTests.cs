using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

/// <summary>
/// Thư mục dữ liệu của app (settings, thư viện, index): mặc định %APPDATA%\PdfReaderApp; đặt
/// TRITHU_DATA_DIR để chạy với dữ liệu riêng (vd chụp ảnh cho PR mà không lộ thư viện thật).
/// </summary>
public class AppDataDirectoryTests
{
    [Fact]
    public void UsesApplicationDataFolder_WhenOverrideIsMissing()
    {
        var dir = AppDataDirectory.Resolve(_ => null, @"C:\Users\me\AppData\Roaming");

        Assert.Equal(@"C:\Users\me\AppData\Roaming\PdfReaderApp", dir);
    }

    [Fact]
    public void UsesOverride_WhenDataDirVariableIsSet()
    {
        var dir = AppDataDirectory.Resolve(
            key => key == AppDataDirectory.OverrideVariable ? @"D:\tmp\trithu-data" : null,
            @"C:\Users\me\AppData\Roaming");

        Assert.Equal(@"D:\tmp\trithu-data", dir);
    }

    [Fact]
    public void IgnoresBlankOverride()
    {
        var dir = AppDataDirectory.Resolve(_ => "   ", @"C:\Roaming");

        Assert.Equal(@"C:\Roaming\PdfReaderApp", dir);
    }
}
