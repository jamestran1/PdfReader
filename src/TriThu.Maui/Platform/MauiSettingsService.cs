using PdfReaderApp.Models;
using PdfReaderApp.Services;
using AppTheme = PdfReaderApp.Models.AppTheme;

namespace TriThu.Maui.Platform;

public sealed class MauiSettingsService : ISettingsService
{
    private const string ApiKeyStorageKey = "openai_api_key";
    private const string ThemePreferenceKey = "theme";

    public string? GetApiKey()
    {
        try
        {
            var key = SecureStorage.Default.GetAsync(ApiKeyStorageKey).GetAwaiter().GetResult();
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (Exception)
        {
            // SecureStorage can throw on platforms where the keychain/keystore
            // is unavailable or locked — degrade to "no key".
            return null;
        }
    }

    public void SaveApiKey(string apiKey)
    {
        SecureStorage.Default.SetAsync(ApiKeyStorageKey, apiKey).GetAwaiter().GetResult();
    }

    public bool HasApiKey() => !string.IsNullOrEmpty(GetApiKey());

    public AppTheme GetThemePreference()
    {
        var raw = Preferences.Default.Get(ThemePreferenceKey, nameof(AppTheme.Light));
        return Enum.TryParse(raw, ignoreCase: true, out AppTheme parsed) ? parsed : AppTheme.Light;
    }

    public void SaveThemePreference(AppTheme theme)
    {
        Preferences.Default.Set(ThemePreferenceKey, theme.ToString());
    }
}
