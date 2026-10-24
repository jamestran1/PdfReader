using PdfReaderApp.Core;
using SkiaSharp;

namespace PdfReaderApp.Tests;

public class HighlightColorTests
{
    private static readonly SKColor DefaultYellow = new(255, 235, 59, 80);

    [Fact]
    public void Parse_ValidHex_ReturnsColorWithFixedAlpha()
        => Assert.Equal(new SKColor(0x12, 0xAB, 0xEF, 80), HighlightColor.Parse("#12ABEF"));

    [Fact]
    public void Parse_LowercaseHex_IsAccepted()
        => Assert.Equal(new SKColor(0xAB, 0xCD, 0xEF, 80), HighlightColor.Parse("#abcdef"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("12ABEF")]
    [InlineData("#12AB")]
    [InlineData("#12ABEF0")]
    [InlineData("#GGGGGG")]
    public void Parse_InvalidInput_ReturnsDefaultYellow(string? hex)
        => Assert.Equal(DefaultYellow, HighlightColor.Parse(hex));
}
