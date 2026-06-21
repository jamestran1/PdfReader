using System.Text;
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
}
