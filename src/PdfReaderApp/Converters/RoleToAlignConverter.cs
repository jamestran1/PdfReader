using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace PdfReaderApp;

public class RoleToAlignConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string role = value as string ?? "User";
        return role == "AI" ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
