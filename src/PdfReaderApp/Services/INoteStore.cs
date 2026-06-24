using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu ghi chú theo owner_key (v1 = documentId) trong notes.db.</summary>
public interface INoteStore
{
    void EnsureSchema();
    void Add(Note note);
    int Update(string id, string content, long nowUnixMs);
    int Delete(string id);
    IReadOnlyList<Note> GetForOwner(string ownerKey);
    /// <summary>Chuyển tất cả ghi chú có owner_key=<paramref name="oldKey"/> sang <paramref name="newKey"/>.
    /// Trả về số dòng cập nhật. Idempotent: gọi lại không thay đổi gì nếu không còn dòng nào với oldKey.</summary>
    int ReassignOwner(string oldKey, string newKey);
}
