using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SearchSnippetBuilderTests
{
    [Fact]
    public void Build_PreservesDiacritics()
    {
        string text = "Hợp đồng bảo hiểm có hiệu lực từ ngày ký kết";
        string snip = SearchSnippetBuilder.Build(text, "bao hiem");
        Assert.Contains("bảo hiểm", snip);
    }

    [Fact]
    public void Build_AddsEllipsisWhenTruncated()
    {
        string text = new string('a', 60) + " bảo hiểm " + new string('b', 60);
        string snip = SearchSnippetBuilder.Build(text, "bao hiem", contextChars: 10);
        Assert.StartsWith("...", snip);
        Assert.EndsWith("...", snip);
    }

    [Fact]
    public void Build_NoMatch_ReturnsNonEmptyHead()
    {
        string text = "Một đoạn văn bản dài để kiểm tra fallback";
        string snip = SearchSnippetBuilder.Build(text, "khongcotrongday");
        Assert.StartsWith("Một đoạn", snip);
    }

    [Fact]
    public void Build_EmptyText_ReturnsEmpty()
    {
        Assert.Equal("", SearchSnippetBuilder.Build("", "abc"));
    }

    [Fact]
    public void Build_ShortMatchNearStart_NoLeadingEllipsis()
    {
        string snip = SearchSnippetBuilder.Build("bảo hiểm nhân thọ", "bao hiem", contextChars: 40);
        Assert.False(snip.StartsWith("..."));
    }
}
