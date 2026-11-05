using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp.Converters;

/// <summary>Thẻ Note có đang được chọn không: nhận [id của Note, <c>Notes.SelectedNoteId</c>] (#295: bấm <c>[n]</c> của Bản soạn chọn Note).</summary>
public sealed class NoteIsSelectedConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values is [string noteId, string selectedId] && string.Equals(noteId, selectedId, StringComparison.Ordinal);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
