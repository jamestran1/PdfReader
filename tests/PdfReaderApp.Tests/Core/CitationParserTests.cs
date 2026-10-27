using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Core;

public class CitationParserTests
{
    private static readonly IReadOnlyList<RagSource> Sources =
    [
        new(1, "docA", "Hợp đồng", 2),
        new(2, "docB", "Báo cáo", 0),
    ];

    [Fact]
    public void Parse_NoCitation_ReturnsOnePlainSpan()
    {
        var spans = CitationParser.Parse("Không có trích dẫn.", Sources);

        var span = Assert.Single(spans);
        Assert.Equal("Không có trích dẫn.", span.Text);
        Assert.Null(span.Source);
    }

    [Fact]
    public void Parse_EmptyText_ReturnsNoSpans() =>
        Assert.Empty(CitationParser.Parse(string.Empty, Sources));

    [Fact]
    public void Parse_KnownCitation_SplitsIntoTextCitationText()
    {
        var spans = CitationParser.Parse("Phạt 5% [1] theo điều khoản.", Sources);

        Assert.Equal(3, spans.Count);
        Assert.Equal("Phạt 5% ", spans[0].Text);
        Assert.Equal(Sources[0], spans[1].Source);
        Assert.Equal(" theo điều khoản.", spans[2].Text);
    }

    [Fact]
    public void Parse_Citation_LabelShowsTitleAndOneBasedPage()
    {
        var spans = CitationParser.Parse("[1]", Sources);

        Assert.Equal("📖 Hợp đồng - Tr.3", Assert.Single(spans).Text);
    }

    [Fact]
    public void Parse_UnknownNumber_StaysPlainText()
    {
        var spans = CitationParser.Parse("Xem [9] nhé", Sources);

        var span = Assert.Single(spans);
        Assert.Equal("Xem [9] nhé", span.Text);
        Assert.Null(span.Source);
    }

    [Fact]
    public void Parse_AdjacentCitations_ProduceTwoCitationSpans()
    {
        var spans = CitationParser.Parse("Kết luận [1][2].", Sources);

        Assert.Equal(
            [new CitationSpan("Kết luận "), CitationSpan.ForSource(Sources[0]), new CitationSpan(", "),
             CitationSpan.ForSource(Sources[1]), new CitationSpan(".")],
            spans);
    }

    [Fact]
    public void Parse_CommaGroup_ProducesCitationPerNumber()
    {
        var spans = CitationParser.Parse("Kết luận [1, 2].", Sources);

        Assert.Equal(
            [new CitationSpan("Kết luận "), CitationSpan.ForSource(Sources[0]), new CitationSpan(", "),
             CitationSpan.ForSource(Sources[1]), new CitationSpan(".")],
            spans);
    }

    [Fact]
    public void Parse_GroupWithUnknownNumber_KeepsUnknownAsPlainBrackets()
    {
        var spans = CitationParser.Parse("[1, 9]", Sources);

        Assert.Equal(Sources[0], spans[0].Source);
        Assert.Equal("[9]", spans[1].Text);
        Assert.Null(spans[1].Source);
    }

    [Theory]
    [InlineData("Dở dang [")]
    [InlineData("Dở dang [1")]
    [InlineData("Dở dang [1,")]
    public void Parse_PartialCitationWhileStreaming_StaysPlainText(string text)
    {
        var span = Assert.Single(CitationParser.Parse(text, Sources));

        Assert.Equal(text, span.Text);
        Assert.Null(span.Source);
    }

    [Fact]
    public void Parse_PageLabelCitation_IsNotTouched()
    {
        var span = Assert.Single(CitationParser.Parse("Xem [Trang 3].", Sources));

        Assert.Equal("Xem [Trang 3].", span.Text);
    }

    [Fact]
    public void Parse_NoSources_AllPlain()
    {
        var span = Assert.Single(CitationParser.Parse("Có [1] đây", []));

        Assert.Equal("Có [1] đây", span.Text);
    }

    [Theory]
    [InlineData("Ý [1: \"the system fails\"] hết.")]
    [InlineData("Ý [1: “the system fails”] hết.")]
    [InlineData("Ý [1 \"the system fails\"] hết.")]
    [InlineData("Ý [1 : \"the system fails\" ] hết.")]
    public void Parse_CitationWithQuote_KeepsQuoteOnTheSpan(string text)
    {
        var spans = CitationParser.Parse(text, Sources);

        Assert.Equal(3, spans.Count);
        Assert.Equal("Ý ", spans[0].Text);
        Assert.Equal(CitationSpan.ForSource(Sources[0], "the system fails"), spans[1]);
        Assert.Equal(" hết.", spans[2].Text);
    }

    [Fact]
    public void Parse_SameSourceQuotedTwice_EachLinkKeepsItsOwnQuote()
    {
        var spans = CitationParser.Parse("[1: \"một câu\"][1: \"câu khác\"]", Sources);

        var links = spans.Where(s => s.Source is not null).ToList();
        Assert.Equal("một câu", links[0].Quote);
        Assert.Equal("câu khác", links[1].Quote);
    }

    [Fact]
    public void Parse_MultipleNumbersWithOneQuote_BecomeLinksWithoutQuote()
    {
        var spans = CitationParser.Parse("Ý [1, 2: \"câu\"] hết.", Sources);

        var links = spans.Where(s => s.Source is not null).ToList();
        Assert.Equal(2, links.Count);
        Assert.All(links, s => Assert.Null(s.Quote));
        Assert.DoesNotContain(spans, s => s.Text.Contains("câu", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("Ý [1: \"the sys")]
    [InlineData("Ý [1: “the sys")]
    [InlineData("Ý [1: ")]
    [InlineData("Ý [1, 2: \"the")]
    public void Parse_UnclosedQuoteWhileStreaming_HidesTheTail(string text)
    {
        var span = Assert.Single(CitationParser.Parse(text, Sources));

        Assert.Equal("Ý ", span.Text);
        Assert.Null(span.Source);
    }

    [Fact]
    public void Parse_UnclosedQuoteOfUnknownNumber_StaysPlainText()
    {
        var span = Assert.Single(CitationParser.Parse("Ý [9: \"the sys", Sources));

        Assert.Equal("Ý [9: \"the sys", span.Text);
    }

    [Fact]
    public void Parse_QuoteOfUnknownNumber_StaysPlainText()
    {
        var span = Assert.Single(CitationParser.Parse("Ý [9: \"câu\"] hết.", Sources));

        Assert.Equal("Ý [9: \"câu\"] hết.", span.Text);
    }

    [Fact]
    public void Parse_EmptyQuote_IsLinkWithoutQuote()
    {
        var spans = CitationParser.Parse("[1: \"  \"]", Sources);

        Assert.Equal(CitationSpan.ForSource(Sources[0]), Assert.Single(spans));
    }

    [Theory]
    [InlineData("Ý [1: \"câu một\"] và [2, 3] nữa.", "Ý [1] và [2, 3] nữa.")]
    [InlineData("Ý [1: “câu”][2: \"khác\"].", "Ý [1][2].")]
    [InlineData("Không có trích dẫn [1].", "Không có trích dẫn [1].")]
    [InlineData("Dở dang [1: \"câu chưa đó", "Dở dang ")]
    public void StripQuotes_RemovesQuotesAndKeepsNumbers(string text, string expected) =>
        Assert.Equal(expected, CitationParser.StripQuotes(text));

    [Fact]
    public void ForSource_WithQuote_TooltipHasTitlePageAndQuoteOnNewLine()
    {
        var span = CitationSpan.ForSource(Sources[0], "câu nguyên văn");

        Assert.Equal("Hợp đồng, trang 3\n“câu nguyên văn”", span.Tooltip);
    }

    [Fact]
    public void ForSource_WithoutQuote_TooltipHasTitleAndPageOnly() =>
        Assert.Equal("Hợp đồng, trang 3", CitationSpan.ForSource(Sources[0]).Tooltip);
}
