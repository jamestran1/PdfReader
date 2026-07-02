using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Platform;

public sealed class NullThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;
    public void Apply(AppTheme theme) { CurrentTheme = theme; }
}

public sealed class NullFilePickerService : IFilePickerService
{
    public Task<string?> PickPdfAsync() => Task.FromResult<string?>(null);
}

public sealed class NullSettingsDialogService : ISettingsDialogService
{
    public Task<string?> ShowAndGetApiKeyAsync() => Task.FromResult<string?>(null);
}
