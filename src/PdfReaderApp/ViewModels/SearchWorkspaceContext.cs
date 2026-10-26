using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Callback của tìm kiếm Toàn Workspace: liệt kê Document của Workspace và nhảy sang Document khác.</summary>
public sealed record SearchWorkspaceContext(
    Func<IReadOnlyList<SearchDocument>> GetDocuments,
    CrossDocJump JumpToDocument);
