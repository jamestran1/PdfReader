using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

// S3: chuyển DocumentId -> SolidColorBrush cho chip nhãn tài liệu
public sealed class DocumentIdToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string hex = PdfReaderApp.ViewModels.DocumentChip.ColorHexFor(value as string);
        if (System.Windows.Media.ColorConverter.ConvertFromString(hex) is System.Windows.Media.Color col)
            return new System.Windows.Media.SolidColorBrush(col);
        return System.Windows.Media.Brushes.Gray;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
