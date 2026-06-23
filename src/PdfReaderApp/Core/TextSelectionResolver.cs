using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace PdfReaderApp.Core;

public readonly record struct SelChar(int CharIndex, string Text, Rect Bounds);

public sealed record SelectionResult(string Text, IReadOnlyList<Rect> LineRects);

/// <summary>Tính vùng chọn text theo dòng chữ: từ dải [anchor,focus] (theo CharIndex) trên
/// một trang -> chuỗi text theo thứ tự đọc + các rect gộp theo dòng. Thuần, không phụ thuộc view.</summary>
public static class TextSelectionResolver
{
    public static SelectionResult Resolve(IReadOnlyList<SelChar> chars, int anchorIndex, int focusIndex)
    {
        if (chars == null || chars.Count == 0)
            return new SelectionResult(string.Empty, Array.Empty<Rect>());

        int lo = Math.Min(anchorIndex, focusIndex);
        int hi = Math.Max(anchorIndex, focusIndex);

        var selected = chars.Where(c => c.CharIndex >= lo && c.CharIndex <= hi)
                            .OrderBy(c => c.CharIndex)
                            .ToList();
        if (selected.Count == 0)
            return new SelectionResult(string.Empty, Array.Empty<Rect>());

        var sb = new StringBuilder();
        foreach (var c in selected) sb.Append(c.Text);

        // Gộp rect theo dòng dựa trên CHỒNG LẤN theo chiều dọc (không theo tâm-Y).
        // Bỏ qua ký tự suy biến (chiều cao/rộng <= 0, ví dụ khoảng trắng từ PDFium) để không
        // tách dòng nhầm tại dấu cách. Union các ký tự thật cùng dòng -> một rect liền phủ luôn
        // khoảng trắng giữa từ. Bền với chữ có dấu / cao thấp khác nhau.
        var realChars = selected.Where(c => c.Bounds.Height > 0 && c.Bounds.Width > 0).ToList();
        var lines = new List<Rect>();
        if (realChars.Count == 0)
            return new SelectionResult(sb.ToString(), lines);

        Rect current = realChars[0].Bounds;
        for (int i = 1; i < realChars.Count; i++)
        {
            var b = realChars[i].Bounds;
            double overlap = Math.Min(current.Bottom, b.Bottom) - Math.Max(current.Top, b.Top);
            double minHeight = Math.Min(current.Height, b.Height);
            if (overlap >= minHeight * 0.5)
            {
                current = Rect.Union(current, b);
            }
            else
            {
                lines.Add(current);
                current = b;
            }
        }
        lines.Add(current);

        return new SelectionResult(sb.ToString(), lines);
    }

    public static int NearestCharIndex(IReadOnlyList<SelChar> chars, Point p)
    {
        if (chars == null || chars.Count == 0) return -1;
        int best = -1;
        double bestDist = double.MaxValue;
        foreach (var c in chars)
        {
            double dx = p.X < c.Bounds.Left ? c.Bounds.Left - p.X
                      : p.X > c.Bounds.Right ? p.X - c.Bounds.Right : 0;
            double dy = p.Y < c.Bounds.Top ? c.Bounds.Top - p.Y
                      : p.Y > c.Bounds.Bottom ? p.Y - c.Bounds.Bottom : 0;
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = c.CharIndex; }
        }
        return best;
    }
}
