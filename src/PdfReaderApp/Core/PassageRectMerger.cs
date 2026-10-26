using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>Gộp các ô của Đoạn nguồn cùng một dòng thành một ô liền.</summary>
public static class PassageRectMerger
{
    /// <summary>Sai số (pt) để coi hai ô là cùng dòng: iText trả cùng Y và Height cho các cụm trên một dòng,
    /// chỉ lệch do làm tròn số thực.</summary>
    private const float LineTolerancePt = 0.5f;

    /// <summary>Khe tối đa (pt) giữa hai cụm liền nhau được gộp, tính theo chiều cao dòng: khoảng trắng giữa hai
    /// cụm chỉ cỡ 2 đến 3 pt, còn nửa dòng (khoảng 6 pt ở chữ 12 pt) đủ phủ khe đó mà không nối hai cột chữ.</summary>
    public static float DefaultMaxGapPt(IReadOnlyList<MatchRect> rects)
    {
        ArgumentNullException.ThrowIfNull(rects);
        return rects.Count == 0 ? 0f : rects.Max(r => r.Height) * 0.5f;
    }

    /// <summary>Ô cùng dòng (Y và Height lệch không quá 0,5 pt) có khe ngang không quá <paramref name="maxGapPt"/>
    /// thì gộp thành một ô; ô khác dòng giữ riêng. Kết quả theo dòng từ trên xuống rồi theo X.</summary>
    /// <remarks>Tô theo từng cụm 8 từ để lệch chữ một chỗ chỉ mất cụm đó, nhưng mỗi cụm ra một ô nên giữa hai cụm
    /// cùng dòng hở một khe (thấy rõ ở nền tối). Gộp ô sau khi tìm xong để vẫn giữ tính chịu lệch của cách chia cụm.</remarks>
    public static IReadOnlyList<MatchRect> MergeLineRects(IReadOnlyList<MatchRect> rects, float maxGapPt)
    {
        ArgumentNullException.ThrowIfNull(rects);
        var result = new List<MatchRect>();
        foreach (var line in GroupByLine(rects))
        {
            var current = line[0];
            for (int i = 1; i < line.Count; i++)
            {
                var next = line[i];
                float gap = next.PdfX - (current.PdfX + current.Width);
                if (gap <= maxGapPt)
                {
                    float right = Math.Max(current.PdfX + current.Width, next.PdfX + next.Width);
                    current = current with { Width = right - current.PdfX };
                }
                else
                {
                    result.Add(current);
                    current = next;
                }
            }
            result.Add(current);
        }
        return result;
    }

    // PDF có gốc ở dưới trái nên Y lớn hơn là dòng ở trên.
    private static List<List<MatchRect>> GroupByLine(IReadOnlyList<MatchRect> rects)
    {
        var lines = new List<List<MatchRect>>();
        foreach (var rect in rects.OrderByDescending(r => r.PdfY).ThenBy(r => r.PdfX))
        {
            var line = lines.Find(l => IsSameLine(l[0], rect));
            if (line is null) lines.Add([rect]);
            else line.Add(rect);
        }
        foreach (var line in lines) line.Sort((a, b) => a.PdfX.CompareTo(b.PdfX));
        return lines;
    }

    private static bool IsSameLine(MatchRect a, MatchRect b) =>
        Math.Abs(a.PdfY - b.PdfY) <= LineTolerancePt && Math.Abs(a.Height - b.Height) <= LineTolerancePt;
}
