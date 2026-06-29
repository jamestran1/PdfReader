using System.Globalization;
using System.Windows.Media;
using PdfReaderApp.Converters;
using PdfReaderApp.Models;
using Xunit;

namespace PdfReaderApp.Tests;

public class LibraryCoverBrushConverterTests
{
    private static LibraryItem Item(string id, string? thumbPath)
        => new LibraryItem(id, "Tài liệu", "/lib/x.pdf", thumbPath, 100, 1, 1);

    private static Brush Convert(LibraryItem item)
        => (Brush)new LibraryCoverBrushConverter()
            .Convert(item, typeof(Brush), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void Convert_WhenNoThumbnail_ReturnsGradientDeterministicInDocumentId()
    {
        var first = Convert(Item("doc-abc", null));
        var second = Convert(Item("doc-abc", null));

        var firstGradient = Assert.IsType<LinearGradientBrush>(first);
        var secondGradient = Assert.IsType<LinearGradientBrush>(second);
        Assert.Equal(firstGradient.GradientStops[0].Color, secondGradient.GradientStops[0].Color);
        Assert.Equal(firstGradient.GradientStops[1].Color, secondGradient.GradientStops[1].Color);
    }

    [Fact]
    public void Convert_DifferentDocumentIds_ProduceDifferentGradients()
    {
        var first = (LinearGradientBrush)Convert(Item("doc-abc", null));
        var second = (LinearGradientBrush)Convert(Item("doc-xyz", null));

        Assert.NotEqual(first.GradientStops[0].Color, second.GradientStops[0].Color);
    }

    [Fact]
    public void Convert_WhenThumbnailFileExists_ReturnsImageBrush()
    {
        // 1x1 PNG so BitmapImage can actually decode it.
        byte[] onePixelPng = System.Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        string thumbPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".png");
        System.IO.File.WriteAllBytes(thumbPath, onePixelPng);
        try
        {
            var brush = Convert(Item("doc-abc", thumbPath));
            var imageBrush = Assert.IsType<ImageBrush>(brush);
            Assert.Equal(Stretch.UniformToFill, imageBrush.Stretch);
        }
        finally { System.IO.File.Delete(thumbPath); }
    }
}
