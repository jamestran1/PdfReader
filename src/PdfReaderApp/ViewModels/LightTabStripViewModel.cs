using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Tab Strip nhẹ của Default Workspace (đọc lẻ, ADR 0009): tab Document trỏ vào viewer standalone cộng các Tab Bản soạn.
/// Đứng riêng bên cạnh <see cref="TabSetViewModel"/>, cùng lớp cơ sở <see cref="TabHost"/> nên đóng tab, MRU và khôi phục
/// chạy cùng một mã. Tab Document không đóng được và không hydrate (viewer standalone giữ Document).
/// </summary>
public sealed class LightTabStripViewModel : TabHost
{
    /// <summary>Strip chỉ hiện khi có từ hai tab, không có nút "+".</summary>
    public bool IsStripVisible => Tabs.Count >= 2;

    public LightTabStripViewModel()
    {
        TabsChanged += () => OnPropertyChanged(nameof(IsStripVisible));
    }

    /// <summary>Tab Document của chính Document đang đọc lẻ: không đóng được.</summary>
    internal static DocumentTab CreateDocumentTab(string documentId, string title, string path)
        => new(documentId, title, path) { CanClose = false };

    /// <summary>
    /// Đảm bảo có Tab Document đứng đầu strip và trả về nó. Mở khi Default Workspace có Tab Bản soạn đầu tiên;
    /// Tab Document đã có thì giữ nguyên.
    /// </summary>
    internal DocumentTab EnsureDocumentTab(string documentId, string title, string path)
    {
        if (Items.OfType<DocumentTab>().FirstOrDefault() is { } existing) return existing;
        var tab = CreateDocumentTab(documentId, title, path);
        InsertAndActivate(0, tab);
        return tab;
    }
}
