using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

// PageIndex (0-based, int?) -> chuỗi "Trang N" (N = index + 1). Null -> rỗng.
public sealed class PageBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is int i ? $"Trang {i + 1}" : string.Empty;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
