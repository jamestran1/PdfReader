using PdfReaderApp.Platform;

namespace PdfReaderApp.Wpf.Platform;

public sealed class WpfSettingsDialogService : ISettingsDialogService
{
    public Task<string?> ShowAndGetApiKeyAsync()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ApiKey))
            return Task.FromResult<string?>(window.ApiKey.Trim());

        return Task.FromResult<string?>(null);
    }
}
