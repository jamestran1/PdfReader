using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface ISettingsService
{
    string? GetApiKey(AiProvider provider);

    /// <summary>Lưu key đã mã hoá; chuỗi rỗng nghĩa là xoá key của provider.</summary>
    void SaveApiKey(AiProvider provider, string apiKey);

    bool HasApiKey(AiProvider provider);

    AiModelSettings GetModelSettings();
    void SaveModelSettings(AiModelSettings settings);

    AppTheme GetThemePreference();
    void SaveThemePreference(AppTheme theme);
}
