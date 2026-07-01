using System;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class MaterialDesignThemeService : IThemeService
{
    public AppTheme CurrentTheme { get; private set; } = AppTheme.Light;

    public static string TokenDictionaryFileName(AppTheme theme)
        => theme == AppTheme.Dark ? "TriThuTokens.Dark.xaml" : "TriThuTokens.xaml";

    public void Apply(AppTheme theme)
    {
        SwapTokenDictionary(theme);
        ApplyPalette(theme);
        CurrentTheme = theme;
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
