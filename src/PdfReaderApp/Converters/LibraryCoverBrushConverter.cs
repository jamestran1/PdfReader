using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PdfReaderApp.Models;

namespace PdfReaderApp.Converters;

/// <summary>
/// Ảnh bìa cho card thư viện: thumbnail trang 1 nếu có, ngược lại gradient suy ra
/// xác định từ DocumentId (cùng tài liệu -> cùng màu) -- thay placeholder của design.
/// </summary>
public sealed class LibraryCoverBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var item = value as LibraryItem;
        if (item is { ThumbPath: { Length: > 0 } thumbPath } && File.Exists(thumbPath))
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(thumbPath);
            image.EndInit();
            image.Freeze();
            return new ImageBrush(image) { Stretch = Stretch.UniformToFill };
        }
        return BuildGradient(item?.DocumentId ?? string.Empty);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;

    private static LinearGradientBrush BuildGradient(string documentId)
    {
        int hash = 17;
        foreach (char character in documentId) hash = hash * 31 + character;
        double topHue = (uint)hash % 360;
        double bottomHue = (topHue + 28) % 360;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
        };
        brush.GradientStops.Add(new GradientStop(FromHsl(topHue, 0.32, 0.42), 0));
        brush.GradientStops.Add(new GradientStop(FromHsl(bottomHue, 0.38, 0.28), 1));
        brush.Freeze();
        return brush;
    }

    private static Color FromHsl(double hueDegrees, double saturation, double lightness)
    {
        double chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
        double huePrime = hueDegrees / 60.0;
        double secondComponent = chroma * (1 - Math.Abs(huePrime % 2 - 1));
        double red = 0, green = 0, blue = 0;
        if (huePrime < 1) { red = chroma; green = secondComponent; }
        else if (huePrime < 2) { red = secondComponent; green = chroma; }
        else if (huePrime < 3) { green = chroma; blue = secondComponent; }
        else if (huePrime < 4) { green = secondComponent; blue = chroma; }
        else if (huePrime < 5) { red = secondComponent; blue = chroma; }
        else { red = chroma; blue = secondComponent; }
        double match = lightness - chroma / 2;
        return Color.FromRgb(
            (byte)Math.Round((red + match) * 255),
            (byte)Math.Round((green + match) * 255),
            (byte)Math.Round((blue + match) * 255));
    }
}
