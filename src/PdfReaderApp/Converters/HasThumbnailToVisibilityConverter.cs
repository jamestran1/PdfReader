using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using PdfReaderApp.Models;

namespace PdfReaderApp.Converters;

/// <summary>
/// Hiện tiêu đề đè lên tile CHỈ khi không có thumbnail (bìa thật đã in tiêu đề).
/// </summary>
public sealed class HasThumbnailToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasThumbnail = value is LibraryItem { ThumbPath: { Length: > 0 } thumbPath } && File.Exists(thumbPath);
        return hasThumbnail ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
