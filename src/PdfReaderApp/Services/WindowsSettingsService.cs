using System.IO;
using System.Security.Cryptography;
using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class WindowsSettingsService : ISettingsService
{
    private readonly string _filePath;
    private readonly string _themeFilePath;

    public WindowsSettingsService(string? storageDirectory = null)
    {
        string dir = storageDirectory
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "PdfReaderApp");
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, "settings.dat");
        _themeFilePath = Path.Combine(dir, "theme.pref");
    }

    public string? GetApiKey()
    {
        if (!File.Exists(_filePath)) return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(_filePath);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (CryptographicException)
        {
            return null; // corrupt or written by a different user -- degrade to "no key"
        }
    }

    public void SaveApiKey(string apiKey)
    {
        byte[] plain = Encoding.UTF8.GetBytes(apiKey);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_filePath, encrypted);
    }

    public bool HasApiKey() => !string.IsNullOrEmpty(GetApiKey());

    public AppTheme GetThemePreference()
    {
        try
        {
            if (!File.Exists(_themeFilePath)) return AppTheme.Light;
            string raw = File.ReadAllText(_themeFilePath).Trim();
            return Enum.TryParse(raw, ignoreCase: true, out AppTheme parsed) ? parsed : AppTheme.Light;
        }
        catch (IOException)
        {
            return AppTheme.Light;
        }
    }

    public void SaveThemePreference(AppTheme theme)
        => File.WriteAllText(_themeFilePath, theme.ToString());
}
