using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class PageContextBuilderTests
{
    private static readonly WorkspaceDocument Doc = new("a", "Sách A", "C:\\a.pdf");

    private static TextBlock Block(int page, string text) => new(text, 0, 0, 10, 10, 12, page, "P");

    [Fact]
    public void BuildNumberedAround_NumbersChunksOfPagesInWindowInPageOrder()
    {
        TextBlock[] blocks = [Block(0, "xa"), Block(2, "xc"), Block(3, "xd"), Block(9, "xfar"), Block(1, "xb")];

        var context = PageContextBuilder.BuildNumberedAround(Doc, blocks, currentPageOneBased: 3, window: 1);

        Assert.NotNull(context);
        Assert.Equal(
            [new RagSource(1, "a", "Sách A", 1, "xb"), new RagSource(2, "a", "Sách A", 2, "xc"), new RagSource(3, "a", "Sách A", 3, "xd")],
            context.Sources);
        Assert.StartsWith("[1] Tài liệu: \"Sách A\", Trang: 2\nxb\n\n[2] Tài liệu: \"Sách A\", Trang: 3\nxc", context.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildNumberedAround_SourcePassageIsTheChunkText()
    {
        string longPage = string.Join(" ", Enumerable.Repeat("chữ", 400));

        var context = PageContextBuilder.BuildNumberedAround(Doc, [Block(0, longPage)], 1, 2);

        Assert.NotNull(context);
        Assert.True(context.Sources.Count > 1);
        Assert.All(context.Sources, s => Assert.Equal(0, s.PageIndex));
        Assert.All(context.Sources, s => Assert.Contains(s.Passage!, context.Text, StringComparison.Ordinal));
    }

    [Fact]
    public void BuildNumberedAround_OverBudget_CutsAndOnlyKeptChunksAreNumbered()
    {
        string longPage = string.Join(" ", Enumerable.Repeat("chữ", 400));

        var context = PageContextBuilder.BuildNumberedAround(Doc, [Block(0, longPage)], 1, 2, maxChars: 1000);

        Assert.NotNull(context);
        Assert.True(context.Text.Length <= 1000);
        Assert.Single(context.Sources);
    }

    [Fact]
    public void BuildNumberedAround_NoTextInWindow_ReturnsNull()
        => Assert.Null(PageContextBuilder.BuildNumberedAround(Doc, [Block(9, "xa")], 1, 2));
}
