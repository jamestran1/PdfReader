using System;
using System.Globalization;
using System.Windows.Data;
using MaterialDesignThemes.Wpf;

namespace PdfReaderApp.Converters;

/// <summary>Icon snackbar theo mức độ: lỗi -> Alert, ngược lại -> Check.</summary>
public sealed class NotificationIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? PackIconKind.AlertCircleOutline : PackIconKind.CheckCircleOutline;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
