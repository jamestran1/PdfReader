using System;
using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace PdfReaderApp.Converters;

/// <summary>
/// Hiển thi bieu tuong nguoc voi che do hien tai:
/// - Che do toi (IsDarkMode=true)  -> hien mat troi (WeatherSunny)  de chuyen sang sang
/// - Che do sang (IsDarkMode=false) -> hien mat trang (WeatherNight) de chuyen sang toi
/// Quy tac "opposite theme" theo thiet ke Claude Design.
/// </summary>
public sealed class BoolToThemeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo? culture)
    {
        bool isDarkMode = value is bool b && b;
        // Dark mode active -> show sun so user can switch to light
        // Light mode active -> show moon so user can switch to dark
        return isDarkMode ? PackIconKind.WeatherSunny : PackIconKind.WeatherNight;
    }

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo? culture)
        => throw new NotSupportedException("BoolToThemeIconConverter is one-way only.");
}
