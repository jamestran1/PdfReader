using System.Globalization;
using System.Windows.Data;
using PdfReaderApp.Models;

namespace PdfReaderApp.Converters;

/// <summary>
/// Dòng trên rail trái có đang active không: nhận [id của mục, Tab active], tham số là <see cref="TabKind"/> (<c>x:Static</c>).
/// So theo cả loại tab vì id Document và id Bản soạn là hai không gian id khác nhau.
/// </summary>
public sealed class IsActiveTabItemConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values is not [string itemId, OpenTab activeTab]) return false;
        return parameter is TabKind expectedKind && activeTab.Kind == expectedKind && string.Equals(activeTab.ItemId, itemId, StringComparison.Ordinal);
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
