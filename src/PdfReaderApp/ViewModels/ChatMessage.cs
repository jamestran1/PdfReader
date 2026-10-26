using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    public partial string Role { get; set; } = "User";

    [ObservableProperty]
    public partial string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>Bảng nguồn <c>[n]</c> của câu trả lời Toàn Workspace (rỗng với tin khác); #108 dựng link từ đây.
    /// Không lưu vào lịch sử chat, nên tin nạp lại từ lịch sử không có bảng nguồn.</summary>
    public IReadOnlyList<RagSource> Sources { get; init; } = [];
}
