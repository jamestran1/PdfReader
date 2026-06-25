using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.Models;

/// <summary>Một Tab đang mở trong Open Set của Workspace hiện tại, kèm per-tab view-state.</summary>
public sealed partial class OpenTab : ObservableObject
{
    public string DocumentId { get; }
    public string Title { get; }
    public string Path { get; }

    /// <summary>Trang hiện tại (1-based). Mặc định 1.</summary>
    [ObservableProperty]
    private int _page = 1;

    /// <summary>Mức zoom. Mặc định 1.0.</summary>
    [ObservableProperty]
    private double _zoom = 1.0;

    /// <summary>Vị trí cuộn đã chuẩn hóa trong trang [0..1], best-effort. Mặc định 0.</summary>
    [ObservableProperty]
    private double _scrollNorm = 0.0;

    public OpenTab(string documentId, string title, string path)
    {
        DocumentId = documentId;
        Title = title;
        Path = path;
    }
}
