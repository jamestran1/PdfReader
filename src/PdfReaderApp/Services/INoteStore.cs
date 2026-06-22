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
}
EOF 2>&1
