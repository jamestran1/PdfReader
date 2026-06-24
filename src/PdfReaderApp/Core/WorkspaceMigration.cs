using System.Collections.Generic;
using PdfReaderApp.Services;

namespace PdfReaderApp.Core;

/// <summary>Migration một lần: tạo default workspace cho từng tài liệu trong thư viện
/// và chuyển ghi chú từ owner_key=documentId sang owner_key=workspaceId.
/// Idempotent: gọi nhiều lần không tạo workspace thừa và không di chuyển ghi chú hai lần.</summary>
public static class WorkspaceMigration
{
    /// <summary>
    /// Chạy migration cho danh sách tài liệu đã cho.
    /// Với mỗi tài liệu: tạo (hoặc lấy) default workspace, rồi chuyển ghi chú
    /// có owner_key=documentId sang owner_key=workspace.Id.
    /// </summary>
    public static void Run(
        IWorkspaceStore workspaceStore,
        INoteStore noteStore,
        IEnumerable<(string documentId, string title)> documents,
        long nowUnixMs)
    {
        foreach (var (documentId, title) in documents)
        {
            var ws = workspaceStore.GetOrCreateDefaultForDocument(documentId, title, nowUnixMs);
            // Chuyển ghi chú: nếu owner_key vẫn là documentId -> đổi sang wsId.
            // Nếu đã chuyển rồi (owner_key = wsId) thì ReassignOwner trả 0 -> idempotent.
            noteStore.ReassignOwner(documentId, ws.Id);
        }
    }
}
