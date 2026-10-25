using System.Globalization;
using System.Windows.Data;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp;

/// <summary>Tên hiển thị của <see cref="AiProvider"/> trong ComboBox của cửa sổ Cài đặt.</summary>
public sealed class AiProviderToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is AiProvider provider ? AiModelCatalog.DisplayName(provider) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
