using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfReaderApp.Converters;

/// <summary>
/// Lề nội dung theo WindowState: 0 khi thường, bằng viền resize (7) khi maximized.
/// WindowChrome maximized tràn ra ngoài work area đúng bằng viền resize -> bù lại để nội dung không bị cắt.
/// </summary>
public sealed class WindowStateToContentMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is WindowState.Maximized ? new Thickness(7) : new Thickness(0);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
