using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Tab Bản soạn: đọc Markdown, hoặc Sửa tay và Đổi tên tại chỗ; không giữ handle PDFium nên đứng ngoài hydrate/evict (ADR 0009).</summary>
public sealed partial class CompositionTab : OpenTab
{
    /// <param name="composition">Bản soạn của tab.</param>
    /// <param name="clock">Đồng hồ cho dòng "Hôm nay 14:20"; để trống thì dùng giờ hệ thống. Truyền vào để model không đọc đồng hồ tĩnh (CS-11).</param>
    public CompositionTab(Composition composition, TimeProvider? clock = null)
        : base(composition?.Id ?? throw new ArgumentNullException(nameof(composition)), composition.Title)
    {
        Composition = composition;
        clock ??= TimeProvider.System;
        WhenText = CompositionSources.FormatWhen(composition.CreatedAtUnixMs, clock.GetUtcNow(), clock.LocalTimeZone);
        ApplySources([]);
    }

    public override TabKind Kind => TabKind.Composition;

    public string CompositionId => ItemId;

    /// <summary>Bản soạn mới nhất đã lưu (đổi sau mỗi lần Sửa tự lưu hoặc Đổi tên).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MetaText))]
    [NotifyPropertyChangedFor(nameof(SourcesMetaText))]
    public partial Composition Composition { get; private set; }

    /// <summary>Presentation dùng chung với Chat AI và Note; citation đã gắn nguồn hiện tại.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<MarkdownPresentationBlock> MarkdownBlocks { get; private set; } = [];

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

    /// <summary>Danh sách nguồn theo Note hiện tại, hiện ở cuối văn bản.</summary>
    [ObservableProperty]
    public partial IReadOnlyList<CompositionSourceEntry> Sources { get; private set; } = [];

    /// <summary>Thời điểm sinh dạng ngắn ("Hôm nay 14:20").</summary>
    public string WhenText { get; }

    /// <summary>"Nguồn: {n} Note · {Chat Model} · {thời điểm}" ở đầu phần nguồn.</summary>
    public string SourcesMetaText => CompositionSources.FormatSourcesMeta(Composition.SourceNoteIds.Count, Composition.ChatModel, WhenText);

    /// <summary>Đặt danh sách nguồn mới và dựng lại các link <c>[n]</c>; nội dung <see cref="Composition"/> không đổi.</summary>
    public void ApplySources(IReadOnlyList<CompositionSourceEntry> sources)
    {
        Sources = sources;
        MarkdownBlocks = MarkdownPresentation.ParseBlocks(
            Composition.Content,
            CompositionSources.ToRagSources(sources),
            source => $"[{source.Number}]");
    }

    /// <summary>Nhãn kiểu Bản soạn trên chip.</summary>
    public string KindLabel => Composition.KindLabel;

    /// <summary>Dòng phụ dưới tiêu đề: "{thời điểm} · {n} Note".</summary>
    public string MetaText => CompositionSources.FormatMeta(WhenText, Composition.SourceNoteIds.Count);

    /// <summary>Áp bản đã lưu lên tab: tiêu đề, nội dung hiển thị. Không đụng <see cref="EditText"/> để không mất chữ đang gõ.</summary>
    internal void Apply(Composition saved)
    {
        Composition = saved;
        Title = saved.Title;
        ApplySources(Sources);
    }
}

