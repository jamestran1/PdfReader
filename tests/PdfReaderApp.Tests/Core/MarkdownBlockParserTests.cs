using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class MarkdownBlockParserTests
{
    [Fact]
    public void Parse_HeadingsListItemsAndParagraphs_BecomeBlocksInOrder()
    {
        string markdown = "## Ý chính\n\nMột đoạn văn [1].\n- mục một\n* mục hai\n1. mục ba\n#### Chi tiết\nKết luận";

        var blocks = MarkdownBlockParser.Parse(markdown);

        Assert.Equal(
        [
            new MarkdownBlock(MarkdownBlockKind.Heading, "Ý chính", 2),
            new MarkdownBlock(MarkdownBlockKind.Paragraph, "Một đoạn văn [1]."),
            new MarkdownBlock(MarkdownBlockKind.ListItem, "mục một"),
            new MarkdownBlock(MarkdownBlockKind.ListItem, "mục hai"),
            new MarkdownBlock(MarkdownBlockKind.ListItem, "mục ba"),
            new MarkdownBlock(MarkdownBlockKind.Heading, "Chi tiết", 4),
            new MarkdownBlock(MarkdownBlockKind.Paragraph, "Kết luận"),
        ], blocks);
    }

    [Fact]
    public void Parse_StripsBoldMarkersAndHandlesCrLf()
    {
        var blocks = MarkdownBlockParser.Parse("Có **chữ đậm** đây\r\n- **mục** đậm");

        Assert.Equal(["Có chữ đậm đây", "mục đậm"], [.. blocks.Select(b => b.Text)]);
    }

    [Fact]
    public void Parse_EmptyOrBlankText_GivesNoBlocks()
    {
        Assert.Empty(MarkdownBlockParser.Parse(""));
        Assert.Empty(MarkdownBlockParser.Parse("\n  \n"));
    }

    [Fact]
    public void Parse_HashWithoutSpace_IsAParagraph()
    {
        var block = Assert.Single(MarkdownBlockParser.Parse("#hashtag"));

        Assert.Equal(new MarkdownBlock(MarkdownBlockKind.Paragraph, "#hashtag"), block);
    }
}
