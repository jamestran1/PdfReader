using System.Globalization;
using System.Windows;

namespace PdfReaderApp;

// Tab S1: MultiValueConverter nhận [thisTab, ActiveTab] -> Visible nếu bằng nhau (ReferenceEqual), Collapsed nếu không.
// Dùng trong ItemTemplate viewer-per-tab để chỉ tab active render, còn lại Collapsed.
public sealed class IsActiveTabConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return Visibility.Collapsed;
        return ReferenceEquals(values[0], values[1]) ? Visibility.Visible : Visibility.Collapsed;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => Array.Empty<object>();
}
