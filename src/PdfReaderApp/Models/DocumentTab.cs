using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.Models;

/// <summary>Tab Document: mở một Document PDF, giữ handle PDFium khi hydrate.</summary>
public sealed partial class DocumentTab : OpenTab
{
    public DocumentTab(string documentId, string title, string path)
        : base(documentId, title)
    {
        Path = path;
    }

    public override TabKind Kind => TabKind.Document;

    public string DocumentId => ItemId;
    public string Path { get; }

    /// <summary>
    /// Tab được giữ handle PDFium sống (active hoặc trong nhóm MRU ấm, #48).
    /// false: viewer của tab nhả tài liệu, chỉ còn header + view-state; nạp lại khi kích hoạt.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ViewerSource))]
    public partial bool IsHydrated { get; set; }

    /// <summary>Nguồn tài liệu cho viewer của tab: null khi tab chưa nạp hoặc đã bị evict.</summary>
    public string? ViewerSource => IsHydrated ? Path : null;
}
