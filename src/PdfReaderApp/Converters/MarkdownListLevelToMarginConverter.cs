using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfReaderApp.Converters;

public sealed class MarkdownListLevelToMarginConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int level ? new Thickness(level * 18, 2, 0, 2) : new Thickness(0, 2, 0, 2);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
