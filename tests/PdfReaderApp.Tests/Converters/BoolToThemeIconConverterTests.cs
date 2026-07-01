using MaterialDesignThemes.Wpf;
using PdfReaderApp.Converters;
using Xunit;

namespace PdfReaderApp.Tests.Converters;

public class BoolToThemeIconConverterTests
{
    private readonly BoolToThemeIconConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsSunIcon()
    {
        // Dark mode (true) -> show sun (WeatherSunny) so user can switch to light
        var result = _converter.Convert(true, typeof(PackIconKind), null, null);
        Assert.Equal(PackIconKind.WeatherSunny, result);
    }

    [Fact]
    public void Convert_False_ReturnsMoonIcon()
    {
        // Light mode (false) -> show moon (WeatherNight) so user can switch to dark
        var result = _converter.Convert(false, typeof(PackIconKind), null, null);
        Assert.Equal(PackIconKind.WeatherNight, result);
    }
}
