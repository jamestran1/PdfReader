using System.Collections.Generic;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using Xunit;

namespace PdfReaderApp.Tests.Services;

public class HighlightLineBuilderTests
{
    private static TextBlock Block(string text, float pdfX, float pdfY, float width, float height, int page = 0) =>
        new(text, pdfX, pdfY, width, height, FontSize: 12f, PageIndex: page, StructureType: "P");

    [Fact]
    public void EmptyList_ReturnsNoLines()
    {
        var result = HighlightLineBuilder.BuildLines(new List<TextBlock>());
        Assert.Empty(result);
    }

    [Fact]
    public void SingleBlock_ReturnsSingleLine()
    {
        var blocks = new List<TextBlock>
        {
            Block("Hello", pdfX: 10f, pdfY: 700f, width: 40f, height: 12f)
        };

        var lines = HighlightLineBuilder.BuildLines(blocks);

        Assert.Single(lines);
        Assert.Equal("Hello", lines[0].Text);
        Assert.Equal(10f, lines[0].PdfX);
        Assert.Equal(700f, lines[0].PdfY);
        Assert.Equal(40f, lines[0].Width);
        Assert.Equal(12f, lines[0].Height);
    }

    [Fact]
    public void TwoBlocksSameBaseline_MergedIntoOneLine_LeftToRight()
    {
        // Two blocks on same baseline (same PdfY bucket)
        var blocks = new List<TextBlock>
        {
            Block("world", pdfX: 50f, pdfY: 700f, width: 30f, height: 12f),
            Block("Hello", pdfX: 10f, pdfY: 700f, width: 35f, height: 12f)
        };

        var lines = HighlightLineBuilder.BuildLines(blocks);

        Assert.Single(lines);
        var line = lines[0];
        // Text ordered left-to-right: "Hello" (x=10) before "world" (x=50)
        Assert.Equal("Hello world", line.Text);
        // PdfX = min(10, 50) = 10
        Assert.Equal(10f, line.PdfX);
        // Width = max(50+30, 10+35) - 10 = 80 - 10 = 70
        Assert.Equal(70f, line.Width);
        // PdfY = min(700, 700) = 700
        Assert.Equal(700f, line.PdfY);
        // Height = max(12, 12) = 12
        Assert.Equal(12f, line.Height);
    }

    [Fact]
    public void TwoBlocksDifferentBaselines_ReturnTwoLines()
    {
        // Blocks on clearly different baselines (>2pt apart)
        var blocks = new List<TextBlock>
        {
            Block("Line1", pdfX: 10f, pdfY: 700f, width: 40f, height: 12f),
            Block("Line2", pdfX: 10f, pdfY: 680f, width: 40f, height: 12f)
        };

        var lines = HighlightLineBuilder.BuildLines(blocks);

        Assert.Equal(2, lines.Count);
        // Lines are returned in descending PdfY order (top of page first)
        Assert.Equal("Line1", lines[0].Text);
        Assert.Equal("Line2", lines[1].Text);
    }

    [Fact]
    public void TwoBlocksDifferentBaselines_EachLineHasCorrectUnionBounds()
    {
        var blocks = new List<TextBlock>
        {
            Block("A", pdfX: 10f, pdfY: 700f, width: 20f, height: 12f),
            Block("B", pdfX: 35f, pdfY: 700f, width: 25f, height: 14f), // same line, different height
            Block("C", pdfX: 15f, pdfY: 680f, width: 30f, height: 12f)  // different line
        };

        var lines = HighlightLineBuilder.BuildLines(blocks);

        Assert.Equal(2, lines.Count);

        // First line (y=700): A + B
        var first = lines[0];
        Assert.Equal("A B", first.Text);
        Assert.Equal(10f, first.PdfX);
        // Width = max(10+20, 35+25) - 10 = 60 - 10 = 50
        Assert.Equal(50f, first.Width);
        Assert.Equal(700f, first.PdfY);
        Assert.Equal(14f, first.Height); // max(12, 14)

        // Second line (y=680): C only
        var second = lines[1];
        Assert.Equal("C", second.Text);
        Assert.Equal(15f, second.PdfX);
        Assert.Equal(30f, second.Width);
    }

    [Fact]
    public void BlocksWithSameYWithinBucketTolerance_MergedIntoOneLine()
    {
        // PdfY values 700.0 and 700.5 — within 2pt bucket, should merge
        var blocks = new List<TextBlock>
        {
            Block("foo", pdfX: 10f, pdfY: 700.0f, width: 20f, height: 12f),
            Block("bar", pdfX: 35f, pdfY: 700.5f, width: 20f, height: 12f)
        };

        var lines = HighlightLineBuilder.BuildLines(blocks);

        Assert.Single(lines);
        Assert.Equal("foo bar", lines[0].Text);
    }

    [Fact]
    public void NullInput_ReturnsEmptyList()
    {
        var result = HighlightLineBuilder.BuildLines(null!);
        Assert.Empty(result);
    }
}
