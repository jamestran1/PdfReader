using PdfReaderApp.Core;
using Xunit;

namespace PdfReaderApp.Tests.Core;

/// <summary>Regression (R1): Shorten cắt dư một ký tự nên kết quả dài maxLength + 2.</summary>
public class TitleFormatterTests
{
    [Fact]
    public void Shorten_TitleLongerThanMax_ResultHasMaxLengthCharsPlusEllipsis()
        => Assert.Equal("abcde" + TitleFormatter.Ellipsis, TitleFormatter.Shorten("abcdefgh", 5));

    [Theory]
    [InlineData("abcde", 5)]  // đúng bằng giới hạn -> giữ nguyên
    [InlineData("abc", 5)]    // ngắn hơn giới hạn -> giữ nguyên
    public void Shorten_TitleWithinMax_ReturnsUnchanged(string title, int maxLength)
        => Assert.Equal(title, TitleFormatter.Shorten(title, maxLength));
}
