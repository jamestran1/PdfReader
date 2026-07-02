using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Card cho lưới Workspaces: bọc Workspace + nhãn dẫn xuất (số tài liệu, tài liệu đang đọc, cờ rỗng).</summary>
public sealed record WorkspaceCard(Workspace Workspace, string Label, string? ActiveTitle, bool IsEmpty)
{
    public string Name => Workspace.Name;
    public bool HasActiveTitle => !string.IsNullOrEmpty(ActiveTitle);
}
