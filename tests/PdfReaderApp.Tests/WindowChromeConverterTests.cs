using System.Globalization;
using System.Windows;
using MaterialDesignThemes.Wpf;
using PdfReaderApp.Converters;
using Xunit;

namespace PdfReaderApp.Tests;

public class WindowChromeConverterTests
{
    private static object Margin(WindowState state)
        => new WindowStateToContentMarginConverter()
            .Convert(state, typeof(Thickness), null!, CultureInfo.InvariantCulture);

    private static object Icon(WindowState state)
        => new WindowStateToMaximizeIconConverter()
            .Convert(state, typeof(PackIconKind), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void ContentMargin_WhenMaximized_CompensatesResizeBorder()
        => Assert.Equal(new Thickness(7), (Thickness)Margin(WindowState.Maximized));

    [Fact]
    public void ContentMargin_WhenNormal_IsZero()
        => Assert.Equal(new Thickness(0), (Thickness)Margin(WindowState.Normal));

    [Fact]
    public void MaximizeIcon_WhenNormal_ShowsMaximizeGlyph()
        => Assert.Equal(PackIconKind.WindowMaximize, (PackIconKind)Icon(WindowState.Normal));

    [Fact]
    public void MaximizeIcon_WhenMaximized_ShowsRestoreGlyph()
        => Assert.Equal(PackIconKind.WindowRestore, (PackIconKind)Icon(WindowState.Maximized));
}
