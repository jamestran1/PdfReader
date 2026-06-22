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

        // Gộp rect theo dòng: cùng dòng nếu chênh lệch tâm-Y nhỏ hơn nửa chiều cao ký tự.
        var lines = new List<Rect>();
        Rect current = selected[0].Bounds;
        double lineCenterY = current.Top + current.Height / 2;
        for (int i = 1; i < selected.Count; i++)
        {
            var b = selected[i].Bounds;
            double cy = b.Top + b.Height / 2;
            if (Math.Abs(cy - lineCenterY) <= b.Height / 2)
            {
                current = Rect.Union(current, b);
            }
            else
            {
                lines.Add(current);
                current = b;
                lineCenterY = cy;
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
