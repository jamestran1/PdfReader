using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class WindowsSettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly string _dir;
    private readonly string _modelsFilePath;
    private readonly string _themeFilePath;

    public WindowsSettingsService(string? storageDirectory = null)
    {
        _dir = storageDirectory ?? Core.AppDataDirectory.Current();
        Directory.CreateDirectory(_dir);
        _modelsFilePath = Path.Combine(_dir, "ai-models.json");
        _themeFilePath = Path.Combine(_dir, "theme.pref");
    }

    public string? GetApiKey(AiProvider provider)
    {
        string path = KeyFilePath(provider);
        if (!File.Exists(path)) return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            string key = Encoding.UTF8.GetString(plain);
            return string.IsNullOrEmpty(key) ? null : key;
        }
        catch (CryptographicException)
        {
            return null; // corrupt or written by a different user -- degrade to "no key"
        }
    }

    public void SaveApiKey(AiProvider provider, string apiKey)
    {
        string path = KeyFilePath(provider);
        if (string.IsNullOrEmpty(apiKey))
        {
            File.Delete(path);
            return;
        }

        byte[] plain = Encoding.UTF8.GetBytes(apiKey);
        byte[] encrypted = ProtectedData.Protect(plain, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
    }

    public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(GetApiKey(provider));

    public AiModelSettings GetModelSettings()
    {
        try
        {
            if (!File.Exists(_modelsFilePath)) return AiModelSettings.Default;
            var parsed = JsonSerializer.Deserialize<AiModelSettings>(File.ReadAllText(_modelsFilePath), JsonOptions);
            return IsUsable(parsed) ? parsed : AiModelSettings.Default;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return AiModelSettings.Default; // hỏng hoặc không đọc được: quay về mặc định
        }
    }

    public void SaveModelSettings(AiModelSettings settings) =>
        File.WriteAllText(_modelsFilePath, JsonSerializer.Serialize(settings, JsonOptions));

    public AppTheme GetThemePreference()
    {
        try
        {
            if (!File.Exists(_themeFilePath)) return AppTheme.Light;
            string raw = File.ReadAllText(_themeFilePath).Trim();
            return Enum.TryParse(raw, ignoreCase: true, out AppTheme parsed) ? parsed : AppTheme.Light;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return AppTheme.Light; // unreadable (locked/denied) -- degrade to the default theme
        }
    }

    public void SaveThemePreference(AppTheme theme)
    {
        try
        {
            File.WriteAllText(_themeFilePath, theme.ToString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed theme-preference save must never crash the app; the theme is
            // a non-critical cosmetic setting, so swallow locked/denied write errors.
        }
    }

    private static bool IsUsable([NotNullWhen(true)] AiModelSettings? settings) =>
        settings is not null
        && Enum.IsDefined(settings.ChatProvider)
        && AiModelCatalog.EmbeddingProviders.Contains(settings.EmbeddingProvider)
        && !string.IsNullOrWhiteSpace(settings.ChatModel)
        && !string.IsNullOrWhiteSpace(settings.EmbeddingModel);

    // Key OpenAI giữ tên settings.dat để tương thích bản cũ; provider khác có file riêng.
    private string KeyFilePath(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => Path.Combine(_dir, "settings.dat"),
        AiProvider.Gemini => Path.Combine(_dir, "settings.gemini.dat"),
        AiProvider.Claude => Path.Combine(_dir, "settings.claude.dat"),
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider không được hỗ trợ."),
    };
}
