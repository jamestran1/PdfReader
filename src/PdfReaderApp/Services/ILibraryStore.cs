using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface ILibraryStore
{
    void EnsureSchema();
    void Upsert(LibraryItem item);
    IReadOnlyList<LibraryItem> GetAll();          // sắp xếp last_opened_at giảm dần
#pragma warning disable CA1716 // "Get" trùng từ khoá VB; đổi tên sẽ kéo theo hàng chục chỗ gọi, ứng dụng chỉ dùng C#
    LibraryItem? Get(string documentId);
#pragma warning restore CA1716
    void TouchLastOpened(string documentId, long whenUnix);
    void Remove(string documentId);
}
