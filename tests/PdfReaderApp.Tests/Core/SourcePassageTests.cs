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
}
