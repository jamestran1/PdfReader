using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Core;

public class SourcePassageTests
{
    [Fact]
    public void SplitFragments_LongPassage_GroupsWordsAndFoldsShortTailIntoPreviousGroup()
    {
        var fragments = SourcePassage.SplitFragments("một hai ba bốn năm sáu bảy", wordsPerFragment: 3);

        Assert.Equal(["một hai ba", "bốn năm sáu bảy"], fragments);
    }

    [Fact]
    public void SplitFragments_LineBreaksAndRepeatedSpaces_CountAsOneSeparator()
    {
        var fragments = SourcePassage.SplitFragments("  một\nhai \r\n ba  ", wordsPerFragment: 3);

        Assert.Equal(["một hai ba"], fragments);
    }

    [Fact]
    public void SplitFragments_ShortPassage_IsOneFragment()
    {
        Assert.Equal(["một"], SourcePassage.SplitFragments("một", wordsPerFragment: 3));
    }

    [Fact]
    public void SplitFragments_BlankPassage_IsEmpty()
    {
        Assert.Empty(SourcePassage.SplitFragments(" \n ", wordsPerFragment: 3));
    }

    [Fact]
    public void From_SourceWithPassage_KeepsItsDocumentAndPage()
    {
        var passage = SourcePassage.From(new RagSource(1, "doc", "Sách", 4, "đoạn nguồn"));

        Assert.NotNull(passage);
        Assert.Equal("doc", passage.DocumentId);
        Assert.Equal(4, passage.PageIndex);
        Assert.Equal(["đoạn nguồn"], passage.Fragments);
    }

    [Fact]
    public void From_SourceSavedWithoutPassage_IsNull()
    {
        Assert.Null(SourcePassage.From(new RagSource(1, "doc", "Sách", 4)));
    }

    [Fact]
    public void From_SourceWithQuote_SplitsTheQuoteAndKeepsTheChunkAsFallback()
    {
        var source = new RagSource(1, "doc", "Sách", 4, "cả đoạn dài nguồn");

        var passage = SourcePassage.From(source, "câu trích");

        Assert.NotNull(passage);
        Assert.Equal(["câu trích"], passage.QuoteFragments);
        Assert.Equal(["cả đoạn dài nguồn"], passage.Fragments);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void From_BlankQuote_HasNoQuoteFragments(string? quote)
    {
        var passage = SourcePassage.From(new RagSource(1, "doc", "Sách", 4, "đoạn"), quote);

        Assert.Null(passage!.QuoteFragments);
    }

    [Fact]
    public void From_QuoteOfSourceWithoutPassage_StillHighlightsTheQuote()
    {
        var passage = SourcePassage.From(new RagSource(1, "doc", "Sách", 4), "câu trích");

        Assert.Equal(["câu trích"], passage!.QuoteFragments);
        Assert.Empty(passage.Fragments);
    }

    private static readonly MatchRect RectA = new(1, 1, 1, 1);
    private static readonly MatchRect RectB = new(2, 2, 2, 2);
    private static readonly MatchRect RectChunk = new(9, 9, 9, 9);

    private static IReadOnlyList<MatchRect> FakeFind(IReadOnlyList<string> fragments) =>
        fragments.SelectMany<string, MatchRect>(f => f switch
        {
            "a" => [RectA],
            "b" => [RectB],
            "chunk" => [RectChunk],
            _ => [],
        }).ToList();

    [Fact]
    public void Locate_QuoteMatchesOnPage_ReturnsOnlyTheQuoteRects()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["a", "b"]);

        Assert.Equal([RectA, RectB], passage.Locate(FakeFind));
    }

    [Fact]
    public void Locate_HalfOfQuoteFragmentsMatch_StillUsesTheQuote()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["a", "lạ"]);

        Assert.Equal([RectA], passage.Locate(FakeFind));
    }

    [Fact]
    public void Locate_FewerThanHalfOfQuoteFragmentsMatch_FallsBackToTheWholeChunk()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["a", "lạ", "xa"]);

        Assert.Equal([RectChunk], passage.Locate(FakeFind));
    }

    private static Func<IReadOnlyList<string>, IReadOnlyList<MatchRect>> FindOnly(string matching) =>
        fragments => fragments.Contains(matching, StringComparer.Ordinal) ? [RectA] : fragments.Contains("chunk", StringComparer.Ordinal) ? [RectChunk] : [];

    [Fact]
    public void Locate_OnlyAOneWordFragmentOfALongQuoteMatches_FallsBackToTheWholeChunk()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["a b c d e f g h", "z"]);

        Assert.Equal([RectChunk], passage.Locate(FindOnly("z")));
    }

    [Fact]
    public void Locate_MatchedFragmentsCoverExactlyHalfTheQuoteWords_UsesTheQuote()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["a b c d", "w x y z"]);

        Assert.Equal([RectA], passage.Locate(FindOnly("w x y z")));
    }

    [Fact]
    public void Locate_MatchedFragmentsCoverLessThanHalfTheQuoteWords_FallsBackToTheWholeChunk()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["a b c d e", "x y z"]);

        Assert.Equal([RectChunk], passage.Locate(FindOnly("x y z")));
    }

    [Fact]
    public void Locate_NothingOfQuoteMatches_FallsBackToTheWholeChunk()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"], ["lạ"]);

        Assert.Equal([RectChunk], passage.Locate(FakeFind));
    }

    [Fact]
    public void Locate_NoQuote_HighlightsTheWholeChunk()
    {
        var passage = new SourcePassage("doc", 0, ["chunk"]);

        Assert.Equal([RectChunk], passage.Locate(FakeFind));
    }

    // Mọi viewer theo tab bind cùng một đoạn nguồn: viewer ẩn của Document khác không được cuộn theo (AI review R1, #241).
    [Theory]
    [InlineData("doc", true)]
    [InlineData("other", false)]
    [InlineData(null, false)]
    public void BelongsTo_OnlyTheCitedDocument(string? documentId, bool expected)
    {
        var passage = new SourcePassage("doc", 4, ["đoạn nguồn"]);

        Assert.Equal(expected, passage.BelongsTo(documentId));
    }
}
