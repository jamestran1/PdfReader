namespace PdfReaderApp.ViewModels;

/// <summary>Helper thuần (không phụ thuộc UI): suy ra màu chip và nhãn rút gọn từ thông tin tài liệu.</summary>
public static class DocumentChip
{
    // Màu hex ổn định, suy ra từ documentId (cùng id -> cùng màu). Bảng màu dịu, dễ phân biệt.
    public static string ColorHexFor(string? documentId)
    {
        if (string.IsNullOrEmpty(documentId)) return "#9E9E9E";
        // hash ổn định không phụ thuộc môi trường (KHÔNG dùng string.GetHashCode vì có thể đổi giữa các lần chạy)
        int h = 0; foreach (char c in documentId) h = unchecked(h * 31 + c);
        string[] palette = { "#1E88E5","#43A047","#E53935","#8E24AA","#FB8C00","#00ACC1","#3949AB","#7CB342" };
        int idx = (int)((uint)h % (uint)palette.Length);
        return palette[idx];
    }

    // Nhãn rút gọn từ tiêu đề tài liệu (cắt còn <= maxLen, thêm chữ "..." nếu cắt). Rỗng -> "".
    public static string ShortLabel(string? title, int maxLen = 18)
    {
        if (string.IsNullOrWhiteSpace(title)) return string.Empty;
        title = title.Trim();
        return title.Length <= maxLen ? title : title.Substring(0, maxLen - 1) + "…";
    }
}
