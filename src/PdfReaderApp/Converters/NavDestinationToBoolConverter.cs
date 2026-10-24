using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

public sealed class NavDestinationToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PdfReaderApp.Core.NavDestination d && parameter is string p
           && string.Equals(d.ToString(), p, StringComparison.Ordinal);

    // OneWay: nav rail highlight chỉ đọc đích; Command mới là thứ đổi đích.
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
