namespace PdfReaderApp.Core;

/// <summary>
/// Tính vị trí thả khi kéo sắp lại Tab trên Tab Strip. Thuần số học trên bề rộng các tab nên test được không cần cửa sổ:
/// code-behind chỉ đo bề rộng và toạ độ chuột rồi gọi vào đây.
/// "Khe" (slot) là chỗ giữa hai tab: khe 0 trước tab đầu, khe N sau tab cuối (N = số tab).
/// </summary>
public static class TabDropPosition
{
    /// <summary>
    /// Khe gần con trỏ nhất: con trỏ ở nửa trái của một tab thì khe trước tab đó, ở nửa phải thì khe sau tab đó.
    /// </summary>
    /// <param name="tabWidths">Bề rộng từng tab theo thứ tự hiển thị.</param>
    /// <param name="pointerX">Toạ độ ngang của con trỏ, gốc là mép trái tab đầu.</param>
    public static int SlotAt(IReadOnlyList<double> tabWidths, double pointerX)
    {
        ArgumentNullException.ThrowIfNull(tabWidths);
        double left = 0;
        for (int index = 0; index < tabWidths.Count; index++)
        {
            if (pointerX < left + (tabWidths[index] / 2)) return index;
            left += tabWidths[index];
        }

        return tabWidths.Count;
    }

    /// <summary>Toạ độ ngang của khe <paramref name="slot"/> (chỗ vẽ chỉ báo thả), gốc là mép trái tab đầu.</summary>
    public static double SlotOffset(IReadOnlyList<double> tabWidths, int slot)
    {
        ArgumentNullException.ThrowIfNull(tabWidths);
        double offset = 0;
        for (int index = 0; index < Math.Min(slot, tabWidths.Count); index++)
            offset += tabWidths[index];
        return offset;
    }

    /// <summary>
    /// Chỉ số của tab sau khi thả vào khe <paramref name="slot"/>, tính theo danh sách đã bỏ tab đang kéo
    /// (đây là <c>newIndex</c> của <c>TabHost.Move</c>).
    /// </summary>
    public static int TargetIndex(int dragIndex, int slot) => slot > dragIndex ? slot - 1 : slot;

    /// <summary>Thả vào khe ngay trước hoặc ngay sau chính tab đang kéo thì thứ tự không đổi.</summary>
    public static bool IsNoOp(int dragIndex, int slot) => TargetIndex(dragIndex, slot) == dragIndex;
}
