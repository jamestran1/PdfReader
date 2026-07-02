using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Core;
using SkiaSharp;

namespace PdfReaderApp.Tests.Core;

public class TextSelectionResolverTests
{
    // Hàng 1: "AB" ở y=0; hàng 2: "CD" ở y=20. Mỗi ký tự rộng 10, cao 10.
    private static List<SelChar> Sample() => new()
    {
        new SelChar(0, "A", SKRect.Create(0, 0, 10, 10)),
        new SelChar(1, "B", SKRect.Create(10, 0, 10, 10)),
        new SelChar(2, "C", SKRect.Create(0, 20, 10, 10)),
        new SelChar(3, "D", SKRect.Create(10, 20, 10, 10)),
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
            new SelChar(0, "A", SKRect.Create(0, 0, 10, 10)),   // span Y [0,10]
            new SelChar(1, ".", SKRect.Create(10, 7, 5, 4)),    // span Y [7,11], thấp + lệch tâm-Y
        };

        var r = TextSelectionResolver.Resolve(line, 0, 1);

        Assert.Single(r.LineRects);
        Assert.Equal("A.", r.Text);
    }

    [Fact]
    public void Resolve_SpaceWithDegenerateBounds_KeepsLineContiguous()
    {
        // Khoảng trắng giữa hai từ thường có bounds suy biến (chiều cao 0) từ PDFium.
        // Không được tách dòng tại đó: cả dòng phải là MỘT rect liền phủ luôn khoảng trắng.
        var line = new List<SelChar>
        {
            new SelChar(0, "e", SKRect.Create(0, 0, 8, 10)),
            new SelChar(1, "f", SKRect.Create(8, 0, 8, 10)),
            new SelChar(2, " ", SKRect.Create(16, 5, 4, 0)),   // space: height 0 (suy biến)
            new SelChar(3, "u", SKRect.Create(20, 0, 8, 10)),
            new SelChar(4, "s", SKRect.Create(28, 0, 8, 10)),
        };

        var r = TextSelectionResolver.Resolve(line, 0, 4);

        Assert.Single(r.LineRects);
        // Rect liền trải từ trái 'e' (0) tới phải 's' (36), phủ cả khoảng trắng.
        Assert.Equal(0, r.LineRects[0].Left);
        Assert.Equal(36, r.LineRects[0].Right);
    }

    [Fact]
    public void Resolve_WhitespaceWithMisplacedBounds_DoesNotSplitLine()
    {
        // Khoảng trắng đôi khi có bounds DƯƠNG nhưng đặt lệch (không overlap dải dòng) từ PDFium.
        // Vẫn phải bỏ qua theo nội dung whitespace -> cả dòng là một rect liền.
        var line = new List<SelChar>
        {
            new SelChar(0, "a", SKRect.Create(0, 0, 8, 10)),
            new SelChar(1, "b", SKRect.Create(8, 0, 8, 10)),
            new SelChar(2, " ", SKRect.Create(16, 20, 6, 3)),  // space: height dương nhưng lệch hẳn xuống
            new SelChar(3, "c", SKRect.Create(22, 0, 8, 10)),
            new SelChar(4, "d", SKRect.Create(30, 0, 8, 10)),
        };

        var r = TextSelectionResolver.Resolve(line, 0, 4);

        Assert.Single(r.LineRects);
        Assert.Equal("ab cd", r.Text);   // text vẫn giữ dấu cách
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
        Assert.Equal(3, TextSelectionResolver.NearestCharIndex(Sample(), new SKPoint(12, 22)));
    }

    [Fact]
    public void NearestCharIndex_PointOutside_ReturnsClosest()
    {
        // Xa bên phải hàng 1 -> gần B (index 1).
        Assert.Equal(1, TextSelectionResolver.NearestCharIndex(Sample(), new SKPoint(100, 2)));
    }

    [Fact]
    public void NearestCharIndex_Empty_ReturnsMinusOne()
    {
        Assert.Equal(-1, TextSelectionResolver.NearestCharIndex(new List<SelChar>(), new SKPoint(0, 0)));
    }
}
