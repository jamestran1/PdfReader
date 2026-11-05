namespace PdfReaderApp.ViewModels;

/// <summary>Trạng thái ô tick ba trạng thái từ số đã chọn và tổng.</summary>
internal static class SelectionState
{
    public static bool? Of(int selected, int total)
        => selected == 0 ? false : selected == total ? true : null;

    /// <summary>
    /// Giá trị tick sau khi người dùng bấm ô ba trạng thái. WPF đi true, null, false nên bấm vào ô đang chọn hết gửi null:
    /// phải hiểu là bỏ hết; null gửi khi đang một phần hay không chọn thì là chọn hết.
    /// </summary>
    public static bool Resolve(bool? requested, bool? current) => requested ?? current != true;
}
