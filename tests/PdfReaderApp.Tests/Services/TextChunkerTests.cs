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
        var chunks = TextChunker.Chunk("doc", []);
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
        var chunks = TextChunker.Chunk("mydoc", [B("hi", 0)]);
        Assert.All(chunks, c => Assert.Equal("mydoc", c.DocumentId));
    }

    // --- ChunkPages tests ---

    private static PageText P(string text, int page) => new(page, text);

    [Fact]
    public void ChunkPages_ShortPage_ProducesOneChunk()
    {
        var pages = new List<PageText> { P("hello world", 0) };
        var chunks = TextChunker.ChunkPages("doc", pages);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].PageIndex);
        Assert.Contains("hello world", chunks[0].Text);
    }

    [Fact]
    public void ChunkPages_Empty_ReturnsEmpty()
    {
        var chunks = TextChunker.ChunkPages("doc", []);
        Assert.Empty(chunks);
    }

    [Fact]
    public void ChunkPages_WhitespaceOnlyPage_SkipsPage()
    {
        var pages = new List<PageText> { P("   ", 0), P("real content", 1) };
        var chunks = TextChunker.ChunkPages("doc", pages);

        Assert.Single(chunks);
        Assert.Equal(1, chunks[0].PageIndex);
    }

    [Fact]
    public void ChunkPages_LongPage_SplitsWithOverlap()
    {
        var pages = new List<PageText> { P(new string('x', 2000), 0) };
        var chunks = TextChunker.ChunkPages("doc", pages, maxChars: 900, overlap: 100);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 900));
    }

    [Fact]
    public void ChunkPages_OrdinalsAreSequential()
    {
        var pages = new List<PageText> { P(new string('a', 2000), 0), P("short", 1) };
        var chunks = TextChunker.ChunkPages("doc", pages, maxChars: 900, overlap: 100);

        Assert.Equal(Enumerable.Range(0, chunks.Count).ToList(), chunks.Select(c => c.Ordinal).ToList());
    }

    [Fact]
    public void ChunkPages_PageIndexMatchesSource()
    {
        var pages = new List<PageText> { P("page zero text", 0), P("page one text", 1) };
        var chunks = TextChunker.ChunkPages("doc", pages);

        Assert.Contains(chunks, c => c.PageIndex == 0);
        Assert.Contains(chunks, c => c.PageIndex == 1);
    }

    [Fact]
    public void ChunkPages_AssignsDocumentId()
    {
        var chunks = TextChunker.ChunkPages("mydoc", [P("hi", 0)]);
        Assert.All(chunks, c => Assert.Equal("mydoc", c.DocumentId));
    }

    // Cặp surrogate (emoji, ký tự ngoài BMP) bị cắt giữa chừng sinh chuỗi không hợp lệ: Normalize ném ArgumentException
    // khi tô nguồn trích dẫn (SearchNormalizer.Fold).
    [Theory]
    [InlineData(899)] // cặp surrogate nằm vắt qua ranh giới cuối chunk đầu (900)
    [InlineData(799)] // cặp surrogate nằm vắt qua điểm bắt đầu chunk thứ hai (800)
    public void ChunkPages_SurrogatePairAtBoundary_NeverSplitsPair(int emojiAt)
    {
        string text = new string('a', emojiAt) + "😀" + new string('b', 1500);

        var chunks = TextChunker.ChunkPages("doc", [new PageText(0, text)]);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, c => AssertNoLoneSurrogate(c.Text));
    }

    [Fact]
    public void Chunk_SurrogatePairAtBoundary_NeverSplitsPair()
    {
        string text = new string('a', 899) + "😀" + new string('b', 1500);

        var chunks = TextChunker.Chunk("doc", [B(text, 0)]);

        Assert.All(chunks, c => AssertNoLoneSurrogate(c.Text));
    }

    private static void AssertNoLoneSurrogate(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            if (char.IsHighSurrogate(text[i]))
            {
                Assert.True(i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]), $"surrogate cao lẻ tại {i}");
                i++;
            }
            else
            {
                Assert.False(char.IsLowSurrogate(text[i]), $"surrogate thấp lẻ tại {i}");
            }
        }
    }
}
