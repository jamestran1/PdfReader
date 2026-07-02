using PdfReaderApp.Platform;

namespace PdfReaderApp.Wpf.Platform;

public sealed class WpfSettingsDialogService : ISettingsDialogService
{
    public string? ShowAndGetApiKey()
    {
        var window = new SettingsWindow
        {
            Owner = System.Windows.Application.Current?.MainWindow
        };

        if (window.ShowDialog() == true && !string.IsNullOrWhiteSpace(window.ApiKey))
            return window.ApiKey.Trim();

        return null;
    }
}
