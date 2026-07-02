using PdfReaderApp.Platform;

namespace TriThu.Maui.Platform;

public sealed class MauiSettingsDialogService : ISettingsDialogService
{
    public async Task<string?> ShowAndGetApiKeyAsync()
    {
        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page is null) return null;

        var input = await page.DisplayPromptAsync(
            "API Key", "Nhập OpenAI API Key:", "Lưu", "Hủy");
        return string.IsNullOrWhiteSpace(input) ? null : input.Trim();
    }
}
