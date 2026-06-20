using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class TextChunkerTests
{
    private static TextBlock B(string text, int page) =>
        new(text, 0f, 0f, 0f, 0f, 12f, page, "Paragraph");

    [Fact]
    public void Chunk_ShortSinglePage_ProducesOneChunk()
    {
        var blocks = new List<TextBlock> { B("hello", 0), B("world", 0) };
        var chunks = TextChunker.Chunk("doc", blocks);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].PageIndex);
        Assert.Contains("hello", chunks[0].Text);
        Assert.Contains("world", chunks[0].Text);
    }

    [Fact]
    public void Chunk_Empty_ReturnsEmpty()
    {
        var chunks = TextChunker.Chunk("doc", new List<TextBlock>());
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_NeverSpansPages()
    {
        var blocks = new List<TextBlock> { B(new string('a', 50), 0), B(new string('b', 50), 1) };
        var chunks = TextChunker.Chunk("doc", blocks, maxChars: 900, overlap: 100);

        Assert.All(chunks, c => Assert.True(c.Text.All(ch => ch == 'a') || c.Text.All(ch => ch == 'b') || c.Text.Trim().Length == 0));
        Assert.Contains(chunks, c => c.PageIndex == 0);
        Assert.Contains(chunks, c => c.PageIndex == 1);
    }

    [Fact]
    public void Chunk_LongPage_SplitsWithOverlap()
    {
        var blocks = new List<TextBlock> { B(new string('x', 2000), 0) };
        var chunks = TextChunker.Chunk("doc", blocks, maxChars: 900, overlap: 100);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 900));
        // ordinals are sequential from 0
        Assert.Equal(Enumerable.Range(0, chunks.Count).ToList(), chunks.Select(c => c.Ordinal).ToList());
    }

    [Fact]
    public void Chunk_AssignsDocumentId()
    {
        var chunks = TextChunker.Chunk("mydoc", new List<TextBlock> { B("hi", 0) });
        Assert.All(chunks, c => Assert.Equal("mydoc", c.DocumentId));
    }
}
