using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AccentInsensitiveRegexTests
{
    [Fact]
    public void Matches_AccentedText_FromFoldedQuery()
    {
        var p = AccentInsensitiveRegex.BuildPattern("kinh hanh"); // typed without diacritics
        Assert.Matches(p, "Trước khi đi kinh hành ta phải");
    }

    [Fact]
    public void Matches_IntraWordSpacing()
    {
        var p = AccentInsensitiveRegex.BuildPattern("kinh hành");
        Assert.Matches(p, "k i n h   h à n h"); // per-glyph extraction spacing
    }

    [Fact]
    public void DoesNotMatch_DifferentPhrase()
    {
        var p = AccentInsensitiveRegex.BuildPattern("kinh hành");
        Assert.DoesNotMatch(p, "đi vào kinh thành lớn");
    }

    [Fact]
    public void CaseInsensitive()
    {
        var p = AccentInsensitiveRegex.BuildPattern("kinh");
        Assert.Matches(p, "KINH HÀNH");
    }

    [Fact]
    public void EmptyQuery_ReturnsEmptyPattern()
    {
        Assert.Equal("", AccentInsensitiveRegex.BuildPattern("   "));
    }
}
