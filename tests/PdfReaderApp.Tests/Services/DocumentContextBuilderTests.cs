using System.Collections.Generic;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class DocumentContextBuilderTests
{
    private static TextBlock Block(string text, int pageIndex) =>
        new(text, 0f, 0f, 0f, 0f, 12f, pageIndex, "Paragraph");

    private static List<TextBlock> FivePages() => new()
    {
        Block("page0", 0), Block("page1", 1), Block("page2", 2),
        Block("page3", 3), Block("page4", 4)
    };

    [Fact]
    public void BuildAround_IncludesWindowPagesOnly()
    {
        // currentPage=3 (1-based) -> index 2; window 1 -> indices 1,2,3
        var ctx = DocumentContextBuilder.BuildAround(FivePages(), currentPageOneBased: 3, window: 1);

        Assert.Contains("page1", ctx);
        Assert.Contains("page2", ctx);
        Assert.Contains("page3", ctx);
        Assert.DoesNotContain("page0", ctx);
        Assert.DoesNotContain("page4", ctx);
    }

    [Fact]
    public void BuildAround_FirstPage_DoesNotFailOnNegativeLowerBound()
    {
        var ctx = DocumentContextBuilder.BuildAround(FivePages(), currentPageOneBased: 1, window: 2);

        Assert.Contains("page0", ctx);
        Assert.Contains("page2", ctx);
        Assert.DoesNotContain("page3", ctx);
    }

    [Fact]
    public void BuildAround_EmptyBlocks_ReturnsEmptyString()
    {
        var ctx = DocumentContextBuilder.BuildAround(new List<TextBlock>(), currentPageOneBased: 1, window: 2);
        Assert.Equal(string.Empty, ctx);
    }

    [Fact]
    public void BuildAround_TruncatesToMaxChars()
    {
        var blocks = new List<TextBlock> { Block(new string('x', 100), 0) };
        var ctx = DocumentContextBuilder.BuildAround(blocks, currentPageOneBased: 1, window: 0, maxChars: 10);
        Assert.True(ctx.Length <= 10);
        Assert.Equal(new string('x', 10), ctx);
    }

    [Fact]
    public void BuildAround_NegativeMaxChars_ReturnsEmpty()
    {
        var blocks = new List<TextBlock> { Block("hello world", 0) };
        var ctx = DocumentContextBuilder.BuildAround(blocks, currentPageOneBased: 1, window: 0, maxChars: -1);
        Assert.Equal(string.Empty, ctx);
    }

    [Fact]
    public void BuildAround_DoesNotSplitSurrogatePair()
    {
        // "😀" is U+1F600, encoded as two UTF-16 code units (high + low surrogate).
        // Build a string of 9 'x' chars followed by "😀" so the high surrogate lands at index 9
        // (i.e. exactly at the cut point when maxChars=10).
        // The method must back up one char and return 9 'x' chars instead of splitting the pair.
        string emoji = "\U0001F600"; // high surrogate at [0], low surrogate at [1]
        string text = new string('x', 9) + emoji; // length = 11; high surrogate is at index 9
        var blocks = new List<TextBlock> { Block(text, 0) };
        var ctx = DocumentContextBuilder.BuildAround(blocks, currentPageOneBased: 1, window: 0, maxChars: 10);

        // Result must not end with a lone high surrogate
        Assert.False(ctx.Length > 0 && char.IsHighSurrogate(ctx[^1]),
            "Result must not end with an unpaired high surrogate.");
    }
}
