using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu Bản soạn theo owner_key (id Workspace) trong compositions.db (ADR 0002).</summary>
public interface ICompositionStore
{
    void EnsureSchema();
    void Add(Composition composition);

    /// <summary>Bản soạn của Workspace, mới nhất trước (thứ tự trên rail).</summary>
    IReadOnlyList<Composition> GetForOwner(string ownerKey);

    /// <summary>Ghi tiêu đề và nội dung mới của một Bản soạn kèm thời điểm sửa. Trả về số dòng đã sửa.</summary>
    int Update(string id, string title, string content, long updatedAtUnixMs);

    /// <summary>Xoá một Bản soạn. Trả về số dòng đã xoá.</summary>
    int Delete(string id);

    /// <summary>Xoá mọi Bản soạn của Workspace. Trả về số dòng đã xoá.</summary>
    int DeleteForOwner(string ownerKey);
}
