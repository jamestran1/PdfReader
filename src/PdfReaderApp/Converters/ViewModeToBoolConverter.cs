using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

// Checks a PdfViewMode against the mode name passed as ConverterParameter, for radio-style toggles.
public sealed class ViewModeToBoolConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is PdfReaderApp.Core.PdfViewMode m && parameter is string p
           && string.Equals(m.ToString(), p, StringComparison.Ordinal);

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true && parameter is string p
           && Enum.TryParse<PdfReaderApp.Core.PdfViewMode>(p, out var m) ? m : Binding.DoNothing;
}
