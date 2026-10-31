using System.Runtime.InteropServices;
using System.Windows;

namespace PdfReaderApp.Core;

/// <summary>Hộp thoại và clipboard thật của Windows, nối vào ViewModel qua delegate để test thay bằng fake.</summary>
internal static class DesktopPrompts
{
    /// <summary>Hỏi xác nhận xoá Bản soạn; mặc định chọn Không.</summary>
    internal static bool ConfirmDeleteComposition(string title)
        => MessageBox.Show(
            $"Xoá Bản soạn “{title}”? Không thể hoàn tác.",
            "Xoá Bản soạn", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

    /// <summary>Đặt văn bản lên clipboard. Clipboard có thể đang bị app khác giữ: thử lại ngắn rồi bỏ qua, không làm sập app.</summary>
    internal static void CopyText(string text)
    {
        try
        {
            Clipboard.SetDataObject(text, copy: true);
        }
        catch (COMException ex)
        {
            System.Diagnostics.Trace.TraceWarning($"DesktopPrompts: không ghi được clipboard: {ex}");
        }
    }
}
