using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.Models;

/// <summary>Một Tab đang mở trong Open Set của Workspace hiện tại, kèm per-tab view-state (ADR 0009: lớp cơ sở).</summary>
public abstract partial class OpenTab : ObservableObject
{
    /// <summary>Loại tab.</summary>
    public abstract TabKind Kind { get; }

    /// <summary>Tab có nút đóng. Tab Document của Tab Strip nhẹ ở Default Workspace thì không (ADR 0009).</summary>
    public virtual bool CanClose => true;

    /// <summary>Id của mục được mở: id Document với Tab Document.</summary>
    public string ItemId { get; }
    /// <summary>Tiêu đề trên tab. Chỉ lớp con đổi được (Tab Bản soạn đổi tên).</summary>
    [ObservableProperty]
    public partial string Title { get; protected set; }

    /// <summary>Trang hiện tại (1-based). Mặc định 1.</summary>
    [ObservableProperty]
    public partial int Page { get; set; } = 1;

    /// <summary>Tổng số trang của tài liệu trong tab. Mặc định 1.</summary>
    [ObservableProperty]
    public partial int TotalPages { get; set; } = 1;

    /// <summary>Mức zoom. Mặc định 1.0.</summary>
    [ObservableProperty]
    public partial double Zoom { get; set; } = 1.0;

    /// <summary>Vị trí cuộn đã chuẩn hóa trong trang [0..1], best-effort. Mặc định 0.</summary>
    [ObservableProperty]
    public partial double ScrollNorm { get; set; } = 0.0;

    /// <summary>Scroll của panel Notes. Mặc định 0.</summary>
    [ObservableProperty]
    public partial double NotesScroll { get; set; } = 0.0;

    /// <summary>Id của note đang chọn. Mặc định null.</summary>
    [ObservableProperty]
    public partial string? SelectedNoteId { get; set; }

    /// <summary>Cờ lọc "chỉ tài liệu này". Mặc định false.</summary>
    [ObservableProperty]
    public partial bool FilterCurrentDocumentOnly { get; set; } = false;

    protected OpenTab(string itemId, string title)
    {
        ItemId = itemId;
        Title = title;
    }
}
