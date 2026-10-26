using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Phần Workspace mà khung chat cần cho hỏi đáp Toàn Workspace.</summary>
/// <param name="GetDocuments">Document thuộc Workspace đang mở (membership), kèm tên và đường dẫn file.</param>
/// <param name="Indexing">Hàng đợi lập chỉ mục nền cho Document chưa có index; ChatViewModel sở hữu và dispose nó.</param>
public sealed record ChatWorkspace(
    Func<IReadOnlyList<WorkspaceDocument>> GetDocuments,
    WorkspaceIndexingRunner Indexing);
