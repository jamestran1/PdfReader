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

        Assert.Equal([Sources[0], Sources[1]], spans.Where(s => s.Source is not null).Select(s => s.Source));
    }

    [Fact]
    public void Parse_CommaGroup_ProducesCitationPerNumber()
    {
        var spans = CitationParser.Parse("Kết luận [1, 2].", Sources);

        Assert.Equal([Sources[0], Sources[1]], spans.Where(s => s.Source is not null).Select(s => s.Source));
        Assert.Equal(".", spans[^1].Text);
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
}
