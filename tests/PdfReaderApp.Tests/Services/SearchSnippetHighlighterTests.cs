using System.Linq;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SearchSnippetHighlighterTests
{
    [Fact]
    public void ComputeSegments_BoldsAccentInsensitiveMatch()
    {
        var segs = SnippetHighlightComputer.ComputeSegments("Hợp đồng bảo hiểm", "bao hiem");
        Assert.Contains(segs, s => s.IsMatch && s.Text == "bảo hiểm");
    }

    [Fact]
    public void ComputeSegments_ConcatEqualsOriginal()
    {
        string text = "Hợp đồng bảo hiểm và bảo hiểm nhân thọ";
        var segs = SnippetHighlightComputer.ComputeSegments(text, "bao hiem");
        Assert.Equal(text, string.Concat(segs.Select(s => s.Text)));
    }

    [Fact]
    public void ComputeSegments_MultipleOccurrences_BoldsEach()
    {
        var segs = SnippetHighlightComputer.ComputeSegments("bảo hiểm và bảo hiểm", "bao hiem");
        Assert.Equal(2, segs.Count(s => s.IsMatch));
    }

    [Fact]
    public void ComputeSegments_EmptyQuery_SingleNonMatchSegment()
    {
        var segs = SnippetHighlightComputer.ComputeSegments("Hợp đồng", "");
        Assert.Single(segs);
        Assert.False(segs[0].IsMatch);
        Assert.Equal("Hợp đồng", segs[0].Text);
    }
}
