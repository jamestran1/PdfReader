using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class TextSelectionResolverTests
{
    // Hàng 1: "AB" ở y=0; hàng 2: "CD" ở y=20. Mỗi ký tự rộng 10, cao 10.
    private static List<SelChar> Sample() => new()
    {
        new SelChar(0, "A", new Rect(0, 0, 10, 10)),
        new SelChar(1, "B", new Rect(10, 0, 10, 10)),
        new SelChar(2, "C", new Rect(0, 20, 10, 10)),
        new SelChar(3, "D", new Rect(10, 20, 10, 10)),
    };

    [Fact]
    public void Resolve_ForwardRange_JoinsTextInReadingOrder()
    {
        var r = TextSelectionResolver.Resolve(Sample(), 0, 2);
        Assert.Equal("ABC", r.Text);
    }

    [Fact]
    public void Resolve_ReversedRange_SameAsForward()
    {
        var fwd = TextSelectionResolver.Resolve(Sample(), 0, 3);
        var rev = TextSelectionResolver.Resolve(Sample(), 3, 0);
        Assert.Equal(fwd.Text, rev.Text);
        Assert.Equal("ABCD", fwd.Text);
    }

    [Fact]
    public void Resolve_SingleChar_WhenAnchorEqualsFocus()
    {
        var r = TextSelectionResolver.Resolve(Sample(), 1, 1);
        Assert.Equal("B", r.Text);
        Assert.Single(r.LineRects);
    }

    [Fact]
    public void Resolve_TwoLines_ProducesTwoLineRects()
    {
        var r = TextSelectionResolver.Resolve(Sample(), 0, 3);
        Assert.Equal(2, r.LineRects.Count);
        // Hàng 1 gộp A+B -> rộng 20 ở y=0; hàng 2 gộp C+D -> y=20.
        Assert.Contains(r.LineRects, rc => rc.Top == 0 && rc.Width == 20);
        Assert.Contains(r.LineRects, rc => rc.Top == 20 && rc.Width == 20);
    }

    [Fact]
    public void Resolve_SameLineVaryingHeights_MergesIntoOneRect()
    {
        // Một dòng: 'A' cao bình thường + dấu câu thấp nằm lệch xuống (chiều cao nhỏ, tâm-Y khác).
        // Cùng dòng trực quan (chồng lấn dọc) nên phải gộp thành MỘT rect liền, không rời ra.
        var line = new List<SelChar>
        {
            new SelChar(0, "A", new Rect(0, 0, 10, 10)),   // span Y [0,10]
            new SelChar(1, ".", new Rect(10, 7, 5, 4)),    // span Y [7,11], thấp + lệch tâm-Y
        };

        var r = TextSelectionResolver.Resolve(line, 0, 1);

        Assert.Single(r.LineRects);
        Assert.Equal("A.", r.Text);
    }

    [Fact]
    public void Resolve_Empty_ReturnsEmpty()
    {
        var r = TextSelectionResolver.Resolve(new List<SelChar>(), 0, 0);
        Assert.Equal("", r.Text);
        Assert.Empty(r.LineRects);
    }

    [Fact]
    public void NearestCharIndex_PointInsideChar_ReturnsThatChar()
    {
        Assert.Equal(3, TextSelectionResolver.NearestCharIndex(Sample(), new Point(12, 22)));
    }

    [Fact]
    public void NearestCharIndex_PointOutside_ReturnsClosest()
    {
        // Xa bên phải hàng 1 -> gần B (index 1).
        Assert.Equal(1, TextSelectionResolver.NearestCharIndex(Sample(), new Point(100, 2)));
    }

    [Fact]
    public void NearestCharIndex_Empty_ReturnsMinusOne()
    {
        Assert.Equal(-1, TextSelectionResolver.NearestCharIndex(new List<SelChar>(), new Point(0, 0)));
    }
}
