using PdfReaderApp.Models;
using PdfReaderApp.Services;
using AppTheme = PdfReaderApp.Models.AppTheme;
using MauiAppTheme = Microsoft.Maui.ApplicationModel.AppTheme;

namespace TriThu.Maui.Platform;

public sealed class MauiThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public void Apply(AppTheme theme)
    {
        if (Application.Current is not null)
        {
            Application.Current.UserAppTheme = theme switch
            {
                AppTheme.Dark => MauiAppTheme.Dark,
                _ => MauiAppTheme.Light,
            };
        }

        CurrentTheme = theme;

        // TODO: Swap custom TriThu token resource dictionaries when ported to MAUI.
    }
}
