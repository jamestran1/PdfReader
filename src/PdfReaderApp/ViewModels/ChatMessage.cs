using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

public partial class ChatMessage : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MarkdownSpans))]
    [NotifyPropertyChangedFor(nameof(MarkdownBlocks))]
    public partial string Role { get; set; } = "User";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Spans))]
    [NotifyPropertyChangedFor(nameof(MarkdownSpans))]
    [NotifyPropertyChangedFor(nameof(MarkdownBlocks))]
    public partial string Content { get; set; } = string.Empty;

    public DateTime Timestamp { get; } = DateTime.Now;

    /// <summary>Bảng nguồn <c>[n]</c> của câu trả lời Toàn Workspace (rỗng với tin khác). Được lưu cùng lượt AI
    /// trong chats.db; tin lưu trước #108 không có bảng nguồn nên hiện là chữ thường.</summary>
    public IReadOnlyList<RagSource> Sources { get; init; } = [];

    /// <summary>Nội dung tách thành chữ thường và trích dẫn bấm được. Tính lại từ <see cref="Content"/> mỗi lần
    /// đổi nên trích dẫn bị chia giữa hai chunk stream chỉ thành link khi đã đủ <c>[n]</c>.</summary>
    public IReadOnlyList<CitationSpan> Spans => CitationParser.Parse(Content, Sources);

    /// <summary>Mô hình hiển thị Markdown inline chỉ dùng cho tin AI; tin người dùng vẫn đi qua <see cref="Spans"/>
    /// như chữ thường. Dữ liệu gốc giữ Markdown raw.</summary>
    public IReadOnlyList<MarkdownInlineSpan> MarkdownSpans =>
        string.Equals(Role, "AI", StringComparison.OrdinalIgnoreCase)
            ? MarkdownPresentation.ParseInline(Content)
            : [];

    /// <summary>Mô hình hiển thị khối CommonMark cho tin AI; mỗi chunk stream làm thuộc tính này báo UI vẽ lại.</summary>
    public IReadOnlyList<MarkdownPresentationBlock> MarkdownBlocks =>
        string.Equals(Role, "AI", StringComparison.OrdinalIgnoreCase)
            ? MarkdownPresentation.ParseBlocks(Content)
            : [];
}
