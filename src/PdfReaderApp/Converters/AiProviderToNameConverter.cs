using System.Globalization;
using System.Windows.Data;
using PdfReaderApp.Models;

namespace PdfReaderApp;

/// <summary>Tên hiển thị của <see cref="AiProvider"/> trong ComboBox của cửa sổ Cài đặt.</summary>
public sealed class AiProviderToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
    {
        AiProvider.OpenAi => "OpenAI",
        AiProvider.Gemini => "Google Gemini",
        AiProvider.Claude => "Anthropic Claude",
        _ => string.Empty,
    };

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
