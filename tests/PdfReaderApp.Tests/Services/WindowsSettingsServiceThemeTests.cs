using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class WindowsSettingsServiceThemeTests : IDisposable
{
    private readonly string _dir;

    public WindowsSettingsServiceThemeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void GetThemePreference_DefaultsToLight_WhenNothingSaved()
    {
        var service = new WindowsSettingsService(_dir);
        Assert.Equal(AppTheme.Light, service.GetThemePreference());
    }

    [Fact]
    public void SaveThenGetThemePreference_RoundTripsDark()
    {
        var service = new WindowsSettingsService(_dir);
        service.SaveThemePreference(AppTheme.Dark);
        Assert.Equal(AppTheme.Dark, service.GetThemePreference());
    }

    [Fact]
    public void GetThemePreference_DefaultsToLight_WhenFileCorrupt()
    {
        File.WriteAllText(Path.Combine(_dir, "theme.pref"), "not-a-theme");
        var service = new WindowsSettingsService(_dir);
        Assert.Equal(AppTheme.Light, service.GetThemePreference());
    }

    [Fact]
    public void ThemePreference_IsStoredSeparatelyFromApiKey()
    {
        var service = new WindowsSettingsService(_dir);
        service.SaveApiKey("sk-test-123");
        service.SaveThemePreference(AppTheme.Dark);

        Assert.Equal("sk-test-123", service.GetApiKey());
        Assert.Equal(AppTheme.Dark, service.GetThemePreference());
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
