using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Tab Bản soạn: hiển thị Markdown chỉ đọc, không giữ handle PDFium nên đứng ngoài hydrate/evict (ADR 0009).</summary>
public sealed partial class CompositionTab : OpenTab
{
    public CompositionTab(Composition composition)
        : base(composition?.Id ?? throw new ArgumentNullException(nameof(composition)), composition.Title)
    {
        Composition = composition;
        Blocks = MarkdownBlockParser.Parse(composition.Content);
        GeneratedText = CompositionSources.FormatGenerated(composition.ChatModel, composition.CreatedAtUnixMs, TimeZoneInfo.Local);
        ApplySources([]);
    }

    public override TabKind Kind => TabKind.Composition;

    public string CompositionId => ItemId;
    public Composition Composition { get; }

    /// <summary>Nội dung đã tách khối để hiển thị Markdown chỉ đọc.</summary>
    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    /// <summary>Khối kèm trích dẫn <c>[n]</c> đã tách; số nguồn chưa biết hoặc đã xoá là chữ thường.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CompositionBlockView> RenderedBlocks { get; private set; } = [];

    /// <summary>Danh sách nguồn theo Note hiện tại, hiện ở cuối văn bản.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CompositionSourceEntry> Sources { get; private set; } = [];

    /// <summary>"Sinh bởi {Chat Model} lúc {thời điểm}".</summary>
    public string GeneratedText { get; }

    /// <summary>Đặt danh sách nguồn mới và dựng lại các link <c>[n]</c>; nội dung <see cref="Composition"/> không đổi.</summary>
    public void ApplySources(IReadOnlyList<CompositionSourceEntry> sources)
    {
        Sources = sources;
        RenderedBlocks = [.. Blocks.Select(b => new CompositionBlockView(b, CompositionSources.ParseCitations(b.Text, sources)))];
    }

    /// <summary>Nhãn kiểu Bản soạn trên chip.</summary>
    public string KindLabel => Composition.Kind switch
    {
        CompositionKind.Summary => "Tóm tắt",
        CompositionKind.Outline => "Outline",
        CompositionKind.Draft => "Soạn thảo",
        _ => Composition.Kind.ToString(),
    };

    /// <summary>Dòng phụ dưới tiêu đề: số Note nguồn và Chat Model đã dùng.</summary>
    public string MetaText => $"{Composition.SourceNoteIds.Count} Note · {Composition.ChatModel}";
}

