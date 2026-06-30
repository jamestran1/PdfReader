using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace PdfReaderApp.Converters;

/// <summary>Glyph cho nút phóng to/khôi phục: maximized -> Restore, ngược lại -> Maximize.</summary>
public sealed class WindowStateToMaximizeIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is WindowState.Maximized ? PackIconKind.WindowRestore : PackIconKind.WindowMaximize;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
