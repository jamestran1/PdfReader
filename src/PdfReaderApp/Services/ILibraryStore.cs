using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface ILibraryStore
{
    void EnsureSchema();
    void Upsert(LibraryItem item);
    IReadOnlyList<LibraryItem> GetAll();          // sap xep last_opened_at giam dan
    LibraryItem? Get(string documentId);
    void TouchLastOpened(string documentId, long whenUnix);
    void Remove(string documentId);
}
