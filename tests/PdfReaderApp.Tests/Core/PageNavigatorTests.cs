using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class PageNavigatorTests
{
    [Theory]
    [InlineData(11, 10, 10)]
    [InlineData(10, 10, 10)]
    [InlineData(0, 10, 1)]
    [InlineData(5, 10, 5)]
    public void ClampPage_KeepsPageWithinOneToPageCount(int page, int pageCount, int expected)
    {
        Assert.Equal(expected, PageNavigator.ClampPage(page, pageCount));
    }
}
