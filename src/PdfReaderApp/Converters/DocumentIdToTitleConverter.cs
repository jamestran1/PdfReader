using System.Globalization;

namespace PdfReaderApp;

// S3: IMultiValueConverter nhận [DocumentId, DocumentTitles] -> nhãn rút gọn cho chip
public sealed class DocumentIdToTitleConverter : System.Windows.Data.IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 2) return string.Empty;
        string? docId = values[0] as string;
        if (string.IsNullOrEmpty(docId)) return string.Empty;
        if (values[1] is IReadOnlyDictionary<string, string> titles && titles.TryGetValue(docId, out var title))
            return PdfReaderApp.ViewModels.DocumentChip.ShortLabel(title);
        // Không có tiêu đề cho documentId này -> nhãn rỗng (không lộ id nội bộ ra UI)
        return string.Empty;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => Array.Empty<object>();
}
