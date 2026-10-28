using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Core;

public class PassageRectMergerTests
{
    [Fact]
    public void MergeLineRects_AdjacentRectsOnOneLine_BecomeOneSpanningRect()
    {
        var merged = PassageRectMerger.MergeLineRects(
            [new MatchRect(72.0f, 321.6f, 86.7f, 13.5f), new MatchRect(160.6f, 321.6f, 182.6f, 13.5f), new MatchRect(345.2f, 321.6f, 125.1f, 13.5f)],
            maxGapPt: 6f);

        var rect = Assert.Single(merged);
        Assert.Equal(72.0f, rect.PdfX, 0.01f);
        Assert.Equal(345.2f + 125.1f - 72.0f, rect.Width, 0.01f);
        Assert.Equal(321.6f, rect.PdfY, 0.01f);
        Assert.Equal(13.5f, rect.Height, 0.01f);
    }

    [Fact]
    public void MergeLineRects_RectsOfTwoColumnsOnOneLineWithGapUnderHalfLine_AreMerged()
    {
        // Ghi lại hành vi hiện tại: merger chỉ nhìn Y và khe ngang, không biết cột. Cột trái kết thúc ở x = 200,
        // cột phải bắt đầu ở x = 203 (khe 3 pt, dòng cao 12 pt nên ngưỡng mặc định là 6 pt) thì hai ô bị nối.
        // Rủi ro chỉ xảy ra khi cả hai cột cùng có ô trong kết quả tìm, mà FindPassageRects chỉ trả ô của chữ khớp đoạn nguồn.
        var rects = new[] { new MatchRect(50f, 100f, 150f, 12f), new MatchRect(203f, 100f, 150f, 12f) };

        var merged = PassageRectMerger.MergeLineRects(rects, PassageRectMerger.DefaultMaxGapPt(rects));

        var rect = Assert.Single(merged);
        Assert.Equal(50f, rect.PdfX, 0.01f);
        Assert.Equal(303f, rect.Width, 0.01f);
    }

    [Fact]
    public void MergeLineRects_FarApartRectsOnOneLine_StaySeparate()
    {
        var merged = PassageRectMerger.MergeLineRects(
            [new MatchRect(72f, 100f, 50f, 12f), new MatchRect(300f, 100f, 50f, 12f)], maxGapPt: 6f);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void MergeLineRects_RectsOnDifferentLines_StaySeparate()
    {
        var merged = PassageRectMerger.MergeLineRects(
            [new MatchRect(72f, 100f, 50f, 12f), new MatchRect(123f, 86f, 50f, 12f)], maxGapPt: 6f);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void MergeLineRects_UnorderedInput_ComesBackByLineThenX()
    {
        var merged = PassageRectMerger.MergeLineRects(
        [
            new MatchRect(300f, 80f, 40f, 12f),
            new MatchRect(60f, 100f, 40f, 12f),
            new MatchRect(61f + 40f, 100.2f, 40f, 12f),
            new MatchRect(60f, 80f, 40f, 12f),
        ], maxGapPt: 6f);

        // PDF gốc ở dưới trái: Y lớn hơn là dòng trên, đọc trước.
        Assert.Equal(3, merged.Count);
        Assert.Equal(100f, merged[0].PdfY, 0.5f);
        Assert.Equal(60f, merged[0].PdfX, 0.01f);
        Assert.Equal(80f, merged[1].PdfY, 0.5f);
        Assert.Equal(60f, merged[1].PdfX, 0.01f);
        Assert.Equal(300f, merged[2].PdfX, 0.01f);
    }

    [Fact]
    public void MergeLineRects_Empty_ReturnsEmpty()
    {
        Assert.Empty(PassageRectMerger.MergeLineRects([], maxGapPt: 6f));
    }
}
