using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace PdfReaderApp;

public class RoleToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string role = value as string ?? "User";
        if (role == "AI")
            return Application.Current.Resources["MaterialDesignSecondaryContainerLow"] ?? Brushes.LightBlue;
        return Application.Current.Resources["MaterialDesignPrimaryContainerLow"] ?? Brushes.LightGray;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
