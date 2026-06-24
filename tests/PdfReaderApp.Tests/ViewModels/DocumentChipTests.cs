using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public class DocumentChipTests
{
    // --- ColorHexFor ---

    [Fact]
    public void ColorHexFor_IsStableForSameId()
    {
        string id = "doc-abc-123";
        string first = DocumentChip.ColorHexFor(id);
        string second = DocumentChip.ColorHexFor(id);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ColorHexFor_ReturnsValueFromPalette()
    {
        string[] palette = { "#1E88E5","#43A047","#E53935","#8E24AA","#FB8C00","#00ACC1","#3949AB","#7CB342" };
        string result = DocumentChip.ColorHexFor("some-document-id");
        Assert.Contains(result, palette);
    }

    [Fact]
    public void ColorHexFor_DiffersForDifferentIds()
    {
        // Với ít nhất 2 id khác nhau trong bảng 8 màu, đảm bảo ít nhất một cặp cho màu khác nhau
        var results = new HashSet<string>();
        string[] ids = { "doc-1", "doc-2", "doc-3", "doc-4", "doc-5", "doc-6", "doc-7", "doc-8", "doc-9" };
        foreach (var id in ids) results.Add(DocumentChip.ColorHexFor(id));
        // Với 9 id khác nhau, ít nhất phải có 2 màu khác nhau (bảng 8 màu)
        Assert.True(results.Count >= 2, "Ít nhất 2 id phải cho màu khác nhau");
    }

    [Fact]
    public void ColorHexFor_NullOrEmpty_ReturnsGrey()
    {
        Assert.Equal("#9E9E9E", DocumentChip.ColorHexFor(null));
        Assert.Equal("#9E9E9E", DocumentChip.ColorHexFor(""));
    }

    // --- ShortLabel ---

    [Fact]
    public void ShortLabel_KeepsShortTitle()
    {
        string result = DocumentChip.ShortLabel("Tài liệu ngắn");
        Assert.Equal("Tài liệu ngắn", result);
    }

    [Fact]
    public void ShortLabel_TruncatesLongTitle_WithEllipsis()
    {
        // maxLen = 18 mặc định; title dài hơn 18 ký tự
        string longTitle = "Đây là tiêu đề rất dài không thể hiển thị hết";
        string result = DocumentChip.ShortLabel(longTitle);
        Assert.True(result.Length <= 18, $"Kết quả quá dài: '{result}'");
        Assert.EndsWith("…", result);
    }

    [Fact]
    public void ShortLabel_ExactlyMaxLen_NotTruncated()
    {
        string title = new string('a', 18);
        string result = DocumentChip.ShortLabel(title, maxLen: 18);
        Assert.Equal(title, result);
        Assert.DoesNotContain("…", result);
    }

    [Fact]
    public void ShortLabel_NullOrWhitespace_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, DocumentChip.ShortLabel(null));
        Assert.Equal(string.Empty, DocumentChip.ShortLabel("   "));
        Assert.Equal(string.Empty, DocumentChip.ShortLabel(""));
    }

    [Fact]
    public void ShortLabel_CustomMaxLen()
    {
        string result = DocumentChip.ShortLabel("Hello World", maxLen: 8);
        Assert.True(result.Length <= 8);
        Assert.EndsWith("…", result);
    }
}
