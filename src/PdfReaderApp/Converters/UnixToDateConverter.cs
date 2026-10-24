using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

// Unix seconds -> chuỗi ngày địa phương (cho nhãn "lần mở cuối" trên thẻ thư viện).
public sealed class UnixToDateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is long secs ? DateTimeOffset.FromUnixTimeSeconds(secs).LocalDateTime.ToString("dd/MM/yyyy", CultureInfo.CurrentCulture) : "";
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
