# Spec — Document Library (thư viện tài liệu)

**Ngày:** 2026-06-22
**Trạng thái:** Đã duyệt thiết kế, chờ review spec → writing-plans
**Nhánh:** `feature/document-library` (tách từ main)

## Mục tiêu

Thay vì chỉ "mở file" mỗi lần, người dùng **import** PDF vào phần mềm — file được **lưu lại** trong thư viện do app quản lý. Các lần sau mở giao diện **thư viện** để chọn lại tài liệu đã import. Hiển thị dạng lưới thẻ có thumbnail bìa.

## Đánh giá độ ảnh hưởng

Mức: **TRUNG BÌNH**, chủ yếu additive.
- **Tái dùng:** `DocumentId` (SHA256 nội dung → dedup), `RenderEngine` (render thumbnail), thư mục `%APPDATA%/PdfReaderApp/`, luồng `LoadDocument` hiện có.
- **Thêm:** thư mục copy file + thumbnail, store thư viện (SQLite riêng), `LibraryService`, UI lưới thư viện, state/commands trên VM, tận dụng nút "Read" đang chết.
- **Không đụng:** AI-index (`SqliteDocumentIndex`/`documents` table), search, view modes, editing. Thư viện độc lập với việc index AI.
- **Rủi ro:** tốn disk (copy file); cần xử lý file lỗi/không đọc được khi import.

## Lưu trữ

- File copy: `%APPDATA%/PdfReaderApp/library/<documentId>.pdf` (id = `DocumentId.FromFile`).
- Thumbnail: `%APPDATA%/PdfReaderApp/library/thumbs/<documentId>.png` (render trang 1 lúc import).
- Dedup: cùng nội dung → cùng id → không tạo bản trùng (chỉ cập nhật `last_opened_at`).

## Persistence — store riêng

DB riêng `%APPDATA%/PdfReaderApp/library.db` (tách khỏi `index.db` của AI để thư viện không phụ thuộc vòng đời index).

Bảng:
```sql
CREATE TABLE IF NOT EXISTS library (
  document_id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  stored_path TEXT NOT NULL,
  thumb_path TEXT,
  page_count INTEGER NOT NULL,
  imported_at INTEGER NOT NULL,   -- unix seconds
  last_opened_at INTEGER NOT NULL
);
```

### `ILibraryStore` + `SqliteLibraryStore`
`src/PdfReaderApp/Services/ILibraryStore.cs`, `SqliteLibraryStore.cs`

```csharp
public interface ILibraryStore
{
    void EnsureSchema();
    void Upsert(LibraryItem item);                 // insert hoặc cập nhật (theo document_id)
    IReadOnlyList<LibraryItem> GetAll();            // sắp xếp last_opened_at giảm dần
    void TouchLastOpened(string documentId, long whenUnix);
    void Remove(string documentId);
    LibraryItem? Get(string documentId);
}
```

### `Models/LibraryItem.cs`
```csharp
public sealed record LibraryItem(
    string DocumentId, string Title, string StoredPath, string? ThumbPath,
    int PageCount, long ImportedAtUnix, long LastOpenedAtUnix);
```

## Import & mở

### `LibraryService` — `src/PdfReaderApp/Services/LibraryService.cs`
Phụ thuộc: `ILibraryStore`, `RenderEngine`, `IPdfDocumentService` (đếm trang) hoặc PDFium.

```csharp
public sealed class LibraryService
{
    // Copy (nếu chưa có) + render thumbnail + page_count + upsert store. Trả LibraryItem.
    LibraryItem Import(string sourcePath, long nowUnix);
    IReadOnlyList<LibraryItem> GetAll();
    void Remove(LibraryItem item);                 // xoá row + file copy + thumb
    void MarkOpened(string documentId, long nowUnix);
}
```
- `Import`: tính `DocumentId.FromFile`. Nếu store đã có id → `TouchLastOpened` + trả item cũ (không copy lại). Nếu chưa: tạo thư mục library/thumbs nếu thiếu, copy file → `<id>.pdf`, render trang 1 → `thumbs/<id>.png`, lấy page_count, `Upsert`. `title` = tên file gốc (không path).
- File nguồn lỗi/không mở được → ném/bắt và báo lỗi, KHÔNG ghi store nửa vời.

### Luồng UI/VM
- Nút **"OPEN PDF"** (toolbar) → mở FileDialog → `LibraryService.Import(path)` → `OpenItem(item)`.
- `OpenItem(item)`: `FilePath = item.StoredPath` (kích hoạt `LoadDocument` sẵn có) → `MarkOpened` → `ShowLibrary = false`.
- `RemoveFromLibrary(item)`: `LibraryService.Remove` → cập nhật collection.

## Giao diện

- VM: `[ObservableProperty] bool _showLibrary;` `ObservableCollection<LibraryItem> Library`. Commands: `ShowLibraryView` (set ShowLibrary=true + nạp lại `Library` từ store), `OpenLibraryItem(LibraryItem)`, `RemoveLibraryItem(LibraryItem)`.
- Nút **"Read"** (rail trái) → `ShowLibraryViewCommand`.
- Khởi động: nếu chưa có file mở → `ShowLibrary = true` và nạp `Library`.
- `MainWindow.xaml` vùng giữa (Grid.Row=1): thêm một lớp thư viện (ScrollViewer + ItemsControl WrapPanel) `Visibility` theo `ShowLibrary`; PdfViewerControl ẩn khi ShowLibrary (và ngược lại). Mỗi thẻ: `Image` (thumb_path), tên, "N trang", lần mở cuối, nút xoá (PackIcon). Bấm thẻ → `OpenLibraryItemCommand`.
- Empty state: nếu `Library` rỗng → text gợi ý "Chưa có tài liệu — bấm OPEN PDF để thêm".

## Chiến lược test

- **`SqliteLibraryStore`** (DB tạm): EnsureSchema; Upsert mới + Upsert cập nhật (không nhân bản theo PK); GetAll sắp xếp last_opened giảm dần; TouchLastOpened đổi mốc; Remove; Get trả null khi không có.
- **`LibraryService.Import`** (thư mục tạm + PDF test tạo bằng iText như các test khác): copy file vào library, tạo store row, page_count đúng; import lại cùng file → KHÔNG copy lần 2 (cùng id), chỉ touch; title = tên file.
- **`MainViewModel`**: `ShowLibraryView` set ShowLibrary=true; `OpenLibraryItem` set FilePath = StoredPath và ShowLibrary=false; `RemoveLibraryItem` bỏ khỏi collection.
- **Thủ công (UI/thumbnail):** lưới thẻ + ảnh bìa hiển thị; nút Read mở thư viện; OPEN PDF import + mở; xoá; dedup khi import trùng.

## Ngoài phạm vi (YAGNI)
- Không sửa/lưu PDF, không annotation, không folder/tag/đổi tên, không đồng bộ cloud, không xác nhận xoá (xoá ngay; có thể thêm sau).
