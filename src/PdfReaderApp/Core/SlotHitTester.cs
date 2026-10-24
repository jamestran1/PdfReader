using System.Windows;

namespace PdfReaderApp.Core;

/// <summary>
/// Tìm trang theo điểm hoặc offset cuộn và giữ điểm neo khi layout đổi. Thuần (chỉ dùng kiểu
/// hình học của WPF) nên test được (TEST-2). Không trả về record mới để control không bị
/// ghép thêm type (CA1506).
/// </summary>
public static class SlotHitTester
{
    /// <summary>Trang chứa điểm màn hình; biên tính là trúng (như <c>Rect.Contains</c>).</summary>
    public static PageSlot? AtPoint(IReadOnlyList<PageSlot> slots, Point screenPoint)
    {
        foreach (var s in slots)
        {
            if (new Rect(s.X, s.Y, s.Width, s.Height).Contains(screenPoint))
                return s;
        }
        return null;
    }

    /// <summary>Đổi điểm màn hình sang toạ độ PDF của <paramref name="slot"/> (chia theo zoom).</summary>
    public static Point ToPdfPoint(PageSlot slot, Point screenPoint, double zoom)
    {
        // Ép về float như code cũ để toạ độ PDF không lệch bit so với trước.
        float scale = (float)zoom;
        return new Point((screenPoint.X - slot.X) / scale, (screenPoint.Y - slot.Y) / scale);
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

    /// <summary>
    /// Chụp điểm neo tại <paramref name="centerY"/>: trả về chỉ số slot (quá trang cuối thì neo vào
    /// trang cuối) và tỉ lệ 0..1 theo chiều cao slot đó.
    /// </summary>
    public static int CaptureAnchor(IReadOnlyList<PageSlot> slots, double centerY, out double fraction)
    {
        for (int i = 0; i < slots.Count; i++)
        {
            var s = slots[i];
            if (centerY < s.Y + s.Height || i == slots.Count - 1)
            {
                fraction = s.Height > 0 ? Math.Clamp((centerY - s.Y) / s.Height, 0, 1) : 0;
                return i;
            }
        }
        fraction = 0;
        return 0;
    }

    /// <summary>Toạ độ Y của điểm neo trong layout mới; false nếu slot neo không còn.</summary>
    public static bool TryRestoreCenterY(IReadOnlyList<PageSlot> slots, int anchorIndex, double fraction, out double centerY)
    {
        if (anchorIndex < 0 || anchorIndex >= slots.Count)
        {
            centerY = 0;
            return false;
        }
        var s = slots[anchorIndex];
        centerY = s.Y + fraction * s.Height;
        return true;
    }
}
