using PdfReaderApp.Core;
using Xunit;

namespace PdfReaderApp.Tests.Core;

/// <summary>Regression (R1): Shorten cắt dư một ký tự nên kết quả dài maxLength + 2.</summary>
public class TitleFormatterTests
{
    [Fact]
    public void Shorten_TitleLongerThanMax_ResultHasMaxLengthCharsPlusEllipsis()
        => Assert.Equal("abcde" + TitleFormatter.Ellipsis, TitleFormatter.Shorten("abcdefgh", 5));

    [Fact]
    public void Shorten_NegativeMax_DoesNotThrowAndReturnsEllipsisOnly()
        => Assert.Equal(TitleFormatter.Ellipsis, TitleFormatter.Shorten("abc", -1));

    [Fact]
    public void Shorten_CutInsideCombiningMark_DropsWholeTextElement()
        => Assert.Equal("ab" + TitleFormatter.Ellipsis, TitleFormatter.Shorten("abéf", 3));

    [Fact]
    public void Shorten_CutInsideSurrogatePair_DropsWholePair()
        => Assert.Equal("ab" + TitleFormatter.Ellipsis, TitleFormatter.Shorten("ab\U0001F600c", 3));

    [Theory]
    [InlineData("abcde", 5)]  // đúng bằng giới hạn -> giữ nguyên
    [InlineData("abc", 5)]    // ngắn hơn giới hạn -> giữ nguyên
    public void Shorten_TitleWithinMax_ReturnsUnchanged(string title, int maxLength)
        => Assert.Equal(title, TitleFormatter.Shorten(title, maxLength));
}
