using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Tab Bản soạn: đọc Markdown, hoặc Sửa tay và Đổi tên tại chỗ; không giữ handle PDFium nên đứng ngoài hydrate/evict (ADR 0009).</summary>
public sealed partial class CompositionTab : OpenTab
{
    public CompositionTab(Composition composition)
        : base(composition?.Id ?? throw new ArgumentNullException(nameof(composition)), composition.Title)
    {
        Composition = composition;
        Blocks = MarkdownBlockParser.Parse(composition.Content);
    }

    public override TabKind Kind => TabKind.Composition;

    public string CompositionId => ItemId;

    /// <summary>Bản soạn mới nhất đã lưu (đổi sau mỗi lần Sửa tự lưu hoặc Đổi tên).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetaText))]
    public partial Composition Composition { get; private set; }

    /// <summary>Nội dung đã tách khối để hiển thị Markdown chỉ đọc.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MarkdownBlock> Blocks { get; private set; }

    /// <summary>Đang ở chế độ Sửa (ô văn bản Markdown thay cho bản đọc).</summary>
    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    /// <summary>Văn bản trong ô Sửa. Gõ vào đây là sửa nội dung; lưu do ViewModel lo.</summary>
    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    /// <summary>Đang đổi tên: tiêu đề thành ô nhập.</summary>
    [ObservableProperty]
    public partial bool IsRenaming { get; set; }

    /// <summary>Văn bản trong ô đổi tên.</summary>
    [ObservableProperty]
    public partial string RenameText { get; set; } = string.Empty;

    /// <summary>Nhãn kiểu Bản soạn trên chip.</summary>
    public string KindLabel => Composition.Kind switch
    {
        CompositionKind.Summary => "Tóm tắt",
        _ => Composition.Kind.ToString(),
    };

    /// <summary>Dòng phụ dưới tiêu đề: số Note nguồn và Chat Model đã dùng.</summary>
    public string MetaText => $"{Composition.SourceNoteIds.Count} Note · {Composition.ChatModel}";

    /// <summary>Áp bản đã lưu lên tab: tiêu đề, nội dung hiển thị. Không đụng <see cref="EditText"/> để không mất chữ đang gõ.</summary>
    internal void Apply(Composition saved)
    {
        Composition = saved;
        Title = saved.Title;
        Blocks = MarkdownBlockParser.Parse(saved.Content);
    }
}
