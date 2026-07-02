using System.Globalization;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace TriThu.Maui.Converters;

public class BoolToVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true;
}

public class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}

public class CountToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int count && count > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PageDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int page ? (page + 1).ToString() : "0";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => int.TryParse(value?.ToString(), out int v) ? v - 1 : 0;
}

public class UnixToDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is long unix)
            return DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime.ToString("dd/MM/yyyy HH:mm");
        return string.Empty;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NullToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ViewModeToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is PdfViewMode mode && parameter is string name)
            return Enum.TryParse<PdfViewMode>(name, out var target) && mode == target;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class NavDestinationToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is NavDestination dest && parameter is string name)
            return Enum.TryParse<NavDestination>(name, out var target) && dest == target;
        return false;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class ZoomPercentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double zoom ? $"{zoom:P0}" : "100%";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class RoleToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string role && role == "user")
            return Application.Current?.Resources.TryGetValue("TriThu.Color.SecondaryContainer", out var c) == true ? c : Colors.LightGray;
        return Application.Current?.Resources.TryGetValue("TriThu.Color.SurfaceTonalLow", out var bg) == true ? bg : Colors.White;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class RoleToAlignConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string role && role == "user" ? LayoutOptions.End : LayoutOptions.Start;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public class PathToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string path && File.Exists(path))
            return ImageSource.FromFile(path);
        return null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
