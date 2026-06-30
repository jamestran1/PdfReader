using System.Globalization;
using MaterialDesignThemes.Wpf;
using PdfReaderApp.Converters;
using Xunit;

namespace PdfReaderApp.Tests;

public class NotificationIconConverterTests
{
    private static object Icon(bool isError)
        => new NotificationIconConverter().Convert(isError, typeof(PackIconKind), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void Success_UsesCheckIcon()
        => Assert.Equal(PackIconKind.CheckCircleOutline, (PackIconKind)Icon(false));

    [Fact]
    public void Error_UsesAlertIcon()
        => Assert.Equal(PackIconKind.AlertCircleOutline, (PackIconKind)Icon(true));
}
