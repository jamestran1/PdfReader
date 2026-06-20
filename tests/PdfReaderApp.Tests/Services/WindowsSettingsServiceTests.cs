using System.IO;
using System.Text;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class WindowsSettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public WindowsSettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void SaveThenGet_ReturnsSameKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey("sk-test-12345");

        Assert.Equal("sk-test-12345", svc.GetApiKey());
    }

    [Fact]
    public void HasApiKey_FalseWhenNothingSaved()
    {
        var svc = new WindowsSettingsService(_dir);
        Assert.False(svc.HasApiKey());
    }

    [Fact]
    public void HasApiKey_TrueAfterSave()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey("sk-test-12345");
        Assert.True(svc.HasApiKey());
    }

    [Fact]
    public void StoredFile_DoesNotContainPlaintextKey()
    {
        var svc = new WindowsSettingsService(_dir);
        svc.SaveApiKey("sk-secret-PLAINTEXT");

        var bytes = File.ReadAllBytes(Path.Combine(_dir, "settings.dat"));
        var asText = Encoding.UTF8.GetString(bytes);

        Assert.DoesNotContain("sk-secret-PLAINTEXT", asText);
    }

    [Fact]
    public void GetApiKey_ReturnsNullWhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "settings.dat"), "not-valid-dpapi-bytes");
        var svc = new WindowsSettingsService(_dir);

        Assert.Null(svc.GetApiKey());
        Assert.False(svc.HasApiKey());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
