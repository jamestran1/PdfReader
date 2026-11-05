using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SearchNormalizerTests
{
    [Fact]
    public void Fold_Viet_Returns_viet()
    {
        Assert.Equal("viet", SearchNormalizer.Fold("Việt")); // Vi + ệ (precomposed NFC)
    }

    [Fact]
    public void Fold_THIEN_Returns_thien()
    {
        Assert.Equal("thien", SearchNormalizer.Fold("THIỀN")); // THIỀN precomposed
    }

    [Fact]
    public void Fold_Duong_Returns_duong()
    {
        // Đường — đ (U+0111) + ường; đ must map to d
        Assert.Equal("duong", SearchNormalizer.Fold("Đường"));
    }

    [Fact]
    public void Fold_TiengViet_Returns_tieng_viet()
    {
        Assert.Equal("tieng viet", SearchNormalizer.Fold("tiếng Việt"));
    }

    [Fact]
    public void Fold_Empty_Returns_Empty()
    {
        Assert.Equal("", SearchNormalizer.Fold(""));
    }

    [Fact]
    public void Fold_NFD_Input_Returns_viet()
    {
        // Build "Việt" in NFD form (base chars + combining marks)
        string nfd = "Việt"; // e + dot below (U+0323) + circumflex (U+0302) => ệ in NFD
        Assert.Equal("viet", SearchNormalizer.Fold(nfd));
    }

    [Fact]
    public void Fold_IsIdempotent()
    {
        string[] inputs = { "Tiếng Việt", "fox", "Đường", "Hello World" };
        foreach (var s in inputs)
        {
            string once = SearchNormalizer.Fold(s);
            string twice = SearchNormalizer.Fold(once);
            Assert.Equal(once, twice);
        }
    }

    [Fact]
    public void Fold_DoubleSpace_CollapsesToSingle()
    {
        Assert.Equal("kinh hanh", SearchNormalizer.Fold("kinh  hành"));
    }

    [Fact]
    public void Fold_Newline_CollapsesToSingleSpace()
    {
        Assert.Equal("kinh hanh", SearchNormalizer.Fold("kinh\nhành"));
    }

    [Fact]
    public void Fold_LeadingTrailingWhitespace_Trimmed()
    {
        Assert.Equal("tieng viet", SearchNormalizer.Fold("  Tiếng   Việt "));
    }

    [Fact]
    public void Fold_TabSeparated_CollapsesToSingleSpace()
    {
        Assert.Equal("kinh hanh", SearchNormalizer.Fold("kinh\t\thành"));
    }

    [Fact]
    public void FoldWithMap_FoldedMatchesFold()
    {
        string[] inputs = { "Tiếng Việt", "Đường", "kinh  hành", "  Tiếng   Việt ", "bảo hiểm", "kinh\t\thành", "kinh\nhành" };
        foreach (var s in inputs)
        {
            var (folded, _) = SearchNormalizer.FoldWithMap(s);
            Assert.Equal(SearchNormalizer.Fold(s), folded);
        }
    }

    [Fact]
    public void FoldWithMap_MapLengthEqualsFoldedLength()
    {
        var (folded, map) = SearchNormalizer.FoldWithMap("  Tiếng   Việt ");
        Assert.Equal(folded.Length, map.Length);
    }

    [Fact]
    public void FoldWithMap_MapsMatchBackToOriginal()
    {
        // "bảo hiểm" -> folded "bao hiem"; folded index 4 ('h') maps to original index 4 ('h')
        string s = "bảo hiểm";
        var (folded, map) = SearchNormalizer.FoldWithMap(s);
        int idx = folded.IndexOf("hiem", StringComparison.Ordinal);
        Assert.True(idx >= 0);
        Assert.Equal('h', s[map[idx]]);
        Assert.Equal('ể', s[map[idx + 2]]); // folded 'e' (3rd char of "hiem") maps to 'ể'
    }

    [Fact]
    public void FoldWithMap_Empty_ReturnsEmpty()
    {
        var (folded, map) = SearchNormalizer.FoldWithMap("");
        Assert.Equal("", folded);
        Assert.Empty(map);
    }

    // PDFium chèn U+FFFE vào vùng chọn tại chỗ gạch nối mềm; string.Normalize ném ArgumentException với nó.
    [Fact]
    public void Fold_NoncharacterFromPdfium_IsDropped()
    {
        Assert.Equal("paraphrase", SearchNormalizer.Fold("para￾phrase"));
    }

    [Fact]
    public void Fold_LoneSurrogate_IsDropped()
    {
        Assert.Equal("ab cd", SearchNormalizer.Fold("a\uD83Db c\uDE00d"));
    }

    [Fact]
    public void Fold_ValidSurrogatePair_IsKept()
    {
        Assert.Equal("vui \U0001F600 qua", SearchNormalizer.Fold("Vui \U0001F600 quá"));
    }

    [Fact]
    public void FoldWithMap_NoncharacterFromPdfium_IsDroppedAndMapStaysAligned()
    {
        const string s = "Para￾phrase";
        var (folded, map) = SearchNormalizer.FoldWithMap(s);

        Assert.Equal("paraphrase", folded);
        Assert.Equal(folded.Length, map.Length);
        Assert.Equal(3, map[3]); // 'a' cuối của "para"
        Assert.Equal(5, map[4]); // 'p' của "phrase" nằm sau ký tự U+FFFE
    }

    [Fact]
    public void FoldWithMap_LoneSurrogate_IsDropped()
    {
        var (folded, map) = SearchNormalizer.FoldWithMap("a\uD83Db");

        Assert.Equal("ab", folded);
        Assert.Equal([0, 2], map);
    }

    [Fact]
    public void FoldWithMap_ValidSurrogatePair_IsKeptAsOneUnitMappedToItsFirstChar()
    {
        var (folded, map) = SearchNormalizer.FoldWithMap("a\U0001F600b");

        Assert.Equal("a\U0001F600b", folded);
        Assert.Equal([0, 1, 1, 3], map); // mỗi nửa surrogate map về ký tự đầu của cặp
    }
}
