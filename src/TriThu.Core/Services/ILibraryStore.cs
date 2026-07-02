using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface ILibraryStore
{
    void EnsureSchema();
    void Upsert(LibraryItem item);
    IReadOnlyList<LibraryItem> GetAll();          // sắp xếp last_opened_at giảm dần
    LibraryItem? Get(string documentId);
    void TouchLastOpened(string documentId, long whenUnix);
    void Remove(string documentId);
}
