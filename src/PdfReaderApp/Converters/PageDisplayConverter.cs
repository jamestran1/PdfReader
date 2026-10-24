using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

public sealed class PageDisplayConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is int n ? n + 1 : value;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
