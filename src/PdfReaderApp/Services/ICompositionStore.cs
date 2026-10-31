using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu Bản soạn theo owner_key (id Workspace) trong compositions.db (ADR 0002).</summary>
public interface ICompositionStore
{
    void EnsureSchema();
    void Add(Composition composition);

    /// <summary>Bản soạn của Workspace, mới nhất trước (thứ tự trên rail).</summary>
    IReadOnlyList<Composition> GetForOwner(string ownerKey);

    /// <summary>Xoá mọi Bản soạn của Workspace. Trả về số dòng đã xoá.</summary>
    int DeleteForOwner(string ownerKey);
}
