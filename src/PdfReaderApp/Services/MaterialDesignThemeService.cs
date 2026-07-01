using System;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class MaterialDesignThemeService : IThemeService
{
    /// <summary>
    /// Global current theme. The SkiaSharp PDF canvas is not a DynamicResource consumer,
    /// so it reads this static property and subscribes to ThemeChanged to repaint on theme switch.
    /// </summary>
    public static AppTheme Current { get; private set; } = AppTheme.Light;

    /// <summary>
    /// Raised after every successful Apply(). Non-XAML consumers (e.g. the SkiaSharp PDF canvas)
    /// subscribe here to trigger a repaint when the theme changes.
    /// </summary>
    public static event EventHandler? ThemeChanged;

    /// <summary>Instance accessor delegating to the static source of truth.</summary>
    public AppTheme CurrentTheme => Current;

    public static string TokenDictionaryFileName(AppTheme theme)
        => theme == AppTheme.Dark ? "TriThuTokens.Dark.xaml" : "TriThuTokens.xaml";

    public void Apply(AppTheme theme)
    {
        SwapTokenDictionary(theme);
        ApplyPalette(theme);
        Current = theme;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static void SwapTokenDictionary(AppTheme theme)
    {
        var dictionaries = Application.Current.Resources.MergedDictionaries;
        var targetUri = new Uri(
            $"pack://application:,,,/PdfReaderApp;component/Themes/{TokenDictionaryFileName(theme)}",
            UriKind.Absolute);

        for (int index = 0; index < dictionaries.Count; index++)
        {
            string? source = dictionaries[index].Source?.OriginalString;
            if (source is not null &&
                (source.EndsWith("Themes/TriThuTokens.xaml", StringComparison.Ordinal) ||
                 source.EndsWith("Themes/TriThuTokens.Dark.xaml", StringComparison.Ordinal)))
            {
                dictionaries[index] = new ResourceDictionary { Source = targetUri };
                return;
            }
        }

        dictionaries.Add(new ResourceDictionary { Source = targetUri });
    }

    private static void ApplyPalette(AppTheme theme)
    {
        var paletteHelper = new PaletteHelper();
        Theme paletteTheme = paletteHelper.GetTheme();

        paletteTheme.SetBaseTheme(theme == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);

        if (theme == AppTheme.Dark)
        {
            paletteTheme.Background = (Color)ColorConverter.ConvertFromString("#0E1513")!;
            paletteTheme.Foreground = (Color)ColorConverter.ConvertFromString("#DEE4E0")!;
        }
        else
        {
            paletteTheme.Background = (Color)ColorConverter.ConvertFromString("#F4FBF7")!;
            paletteTheme.Foreground = (Color)ColorConverter.ConvertFromString("#161D1B")!;
        }

        paletteHelper.SetTheme(paletteTheme);
    }
}
