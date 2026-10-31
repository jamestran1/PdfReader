using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Tab Bản soạn: hiển thị Markdown chỉ đọc, không giữ handle PDFium nên đứng ngoài hydrate/evict (ADR 0009).</summary>
public sealed class CompositionTab : OpenTab
{
    public CompositionTab(Composition composition)
        : base(composition?.Id ?? throw new ArgumentNullException(nameof(composition)), composition.Title)
    {
        Composition = composition;
        Blocks = MarkdownBlockParser.Parse(composition.Content);
    }

    public override TabKind Kind => TabKind.Composition;

    public string CompositionId => ItemId;
    public Composition Composition { get; }

    /// <summary>Nội dung đã tách khối để hiển thị Markdown chỉ đọc.</summary>
    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    /// <summary>Nhãn kiểu Bản soạn trên chip.</summary>
    public string KindLabel => Composition.Kind switch
    {
        CompositionKind.Summary => "Tóm tắt",
        _ => Composition.Kind.ToString(),
    };

    /// <summary>Dòng phụ dưới tiêu đề: số Note nguồn và Chat Model đã dùng.</summary>
    public string MetaText => $"{Composition.SourceNoteIds.Count} Note · {Composition.ChatModel}";
}
