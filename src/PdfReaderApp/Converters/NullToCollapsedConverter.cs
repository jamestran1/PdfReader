using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfReaderApp;

// Giá trị null -> Collapsed, có giá trị -> Visible (ẩn badge khi note không neo trang).
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
