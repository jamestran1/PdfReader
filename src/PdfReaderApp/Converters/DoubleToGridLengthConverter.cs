using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfReaderApp.Converters;

public sealed class DoubleToGridLengthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new GridLength((double)value);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => ((GridLength)value).Value;
}
