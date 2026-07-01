using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class MaterialDesignThemeServiceTests
{
    [Fact]
    public void TokenDictionaryFileName_Light_ReturnsLightTokens()
    {
        Assert.Equal("TriThuTokens.xaml", MaterialDesignThemeService.TokenDictionaryFileName(AppTheme.Light));
    }

    [Fact]
    public void TokenDictionaryFileName_Dark_ReturnsDarkTokens()
    {
        Assert.Equal("TriThuTokens.Dark.xaml", MaterialDesignThemeService.TokenDictionaryFileName(AppTheme.Dark));
    }
}
