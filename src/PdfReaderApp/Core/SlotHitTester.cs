namespace PdfReaderApp.Core;

/// <summary>
/// Tìm trang theo điểm hoặc offset cuộn và giữ điểm neo khi layout đổi. Thuần, không phụ thuộc WPF
/// để test được (TEST-2).
/// </summary>
public static class SlotHitTester
{
    /// <summary>Trang chứa điểm màn hình; biên tính là trúng (như <c>Rect.Contains</c>).</summary>
    public static SlotHit? TryHit(IReadOnlyList<PageSlot> slots, double x, double y, double scale)
    {
        // Ép về float như code cũ để toạ độ PDF không lệch bit so với trước.
        float floatScale = (float)scale;
        foreach (var s in slots)
        {
            bool inside = x >= s.X && x <= s.X + s.Width && y >= s.Y && y <= s.Y + s.Height;
            if (inside)
                return new SlotHit(s, (x - s.X) / floatScale, (y - s.Y) / floatScale);
        }
        return null;
    }

    /// <summary>Trang đầu tiên có <paramref name="offsetY"/> nằm trong khoảng dọc của nó (biên tính là trúng).</summary>
    public static PageSlot? AtOffset(IReadOnlyList<PageSlot> slots, double offsetY)
    {
        foreach (var s in slots)
        {
            if (offsetY >= s.Y && offsetY <= s.Y + s.Height)
                return s;
        }
        return null;
    }

    /// <summary>Chụp điểm neo tại <paramref name="centerY"/>; quá trang cuối thì neo vào trang cuối.</summary>
    public static SlotAnchor CaptureAnchor(IReadOnlyList<PageSlot> slots, double centerY)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (centerY < s.Y + s.Height || i == slots.Count - 1)
            {
                double frac = s.Height > 0 ? Math.Clamp((centerY - s.Y) / s.Height, 0, 1) : 0;
                return new SlotAnchor(i, frac);
            }
        }
        return new SlotAnchor(0, 0);
    }

    /// <summary>Toạ độ Y của điểm neo trong layout mới; null nếu slot neo không còn.</summary>
    public static double? RestoreCenterY(IReadOnlyList<PageSlot> slots, SlotAnchor anchor)
    {
        if (anchor.SlotIndex < 0 || anchor.SlotIndex >= slots.Count) return null;
        var s = slots[anchor.SlotIndex];
        return s.Y + anchor.Fraction * s.Height;
    }
}
