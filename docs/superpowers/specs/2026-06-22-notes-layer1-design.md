# Notes Layer 1 — Nền tảng ghi chú theo sách — Thiết kế

**Ngày:** 2026-06-22
**Trạng thái:** Đã qua review chuyên môn (3 reviewer độc lập), đã chỉnh theo phản biện. Chờ review spec.
**Thuộc epic:** `docs/superpowers/specs/2026-06-22-taking-note-epic.md` (Layer 1).

## Mục tiêu

Mỗi quyển sách có một tập ghi chú dạng thẻ rời, lưu bền vững, tạo/sửa/xóa được, mỗi note tự neo vào trang đang xem (click để nhảy tới), có lọc nhanh theo nội dung. Đặt nền data-model "sẵn sàng workspace" cho các layer sau.

## Quyết định (gồm kết quả review)

1. **Note dạng nhiều thẻ rời** (không phải một cuốn sổ dài).
2. **Panel Notes nằm chung sidebar phải với Chat**, dạng 2 tab dùng `TabControl` MaterialDesign (Chat | Notes).
3. **Mỗi note tự neo trang hiện tại** lúc tạo; click note nhảy tới trang.
4. **Có lọc client-side + sắp theo trang** ngay ở v1.
5. **Seam workspace-ready ngay từ Layer 1:** tách `owner_key` (phạm vi, = documentId ở v1) khỏi `document_id` (anchor, nullable); định danh **GUID**; có `PRAGMA user_version` + nhánh migration.

## Vì sao một số thứ làm khác bản nháp đầu (theo review)

- **`TabControl` MD (bằng chứng từ template v5.1.0):** `ControlTemplate` của `MaterialDesignTabControl` chỉ có một `ContentPresenter x:Name="PART_SelectedContentHost"` (`ContentSource="SelectedContent"`) → nội dung tab không-chọn **gỡ khỏi visual tree**. Hệ quả THẬT chỉ là **có thể reset cuộn/focus** khi chuyển tab; **dữ liệu/stream chat KHÔNG mất** (nằm ở `ChatMessages` trong VM). Quyết định: **dùng `TabControl` MD**; verify cuộn ở GUI; nếu khó chịu thì áp fix keep-alive (template giữ cả hai content host) — không làm trước khi có bằng chứng cần.
- **Reload cả danh sách sau mỗi sửa/xóa** làm mất scroll + focus → cập nhật `ObservableCollection` tại chỗ; chỉ nạp lại khi đổi sách.
- **Khóa chỉ bằng documentId + id autoincrement** trái nguyên tắc epic và khó xuất/chia sẻ về sau → `owner_key` + anchor nullable + GUID.
- **`PageIndex` bare + hash nội dung làm note "mất" khi đổi file:** trong kiến trúc hiện tại library **copy file vào app và không ghi đè**, nên bản lưu *bất biến* → hash ổn định, anchor trang không lệch. Chỉ thành vấn đề khi có *sửa-PDF-ghi-đè* (chưa có). → Hoãn "định danh ổn định + nối phiên bản" (ghi nhận ở epic), không thêm fingerprint trang ở v1.

## Kiến trúc & thành phần

### Model

```
public sealed record Note(
    string Id,            // GUID
    string OwnerKey,      // phạm vi (v1 = documentId của sách)
    string? DocumentId,   // anchor: doc mà note trỏ tới (v1 = documentId của sách)
    int? PageIndex,       // anchor trang 0-based (v1 luôn = trang lúc tạo)
    string Content,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs);
```

GUID + timestamp do tầng VM sinh khi tạo (app code dùng `Guid.NewGuid()`, `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()` thoải mái).

### Store

`INoteStore`:
- `void EnsureSchema();`
- `void Add(Note note);`
- `int Update(string id, string content, long nowUnixMs);` (trả số dòng ảnh hưởng)
- `int Delete(string id);` (trả số dòng ảnh hưởng)
- `IReadOnlyList<Note> GetForOwner(string ownerKey);` (KHÔNG sắp xếp ở store; sắp ở VM)

`SqliteNoteStore : INoteStore` (`notes.db`, connection per-op `Pooling=False`, có `_lock`, đúng pattern `SqliteLibraryStore`/`SqliteChatHistoryStore`):
- `EnsureSchema`:
  ```sql
  CREATE TABLE IF NOT EXISTS note (
    id TEXT PRIMARY KEY,
    owner_key TEXT NOT NULL,
    document_id TEXT,
    page_index INTEGER,
    content TEXT NOT NULL,
    created_at INTEGER NOT NULL,
    updated_at INTEGER NOT NULL);
  CREATE INDEX IF NOT EXISTS ix_note_owner ON note(owner_key);
  ```
  Đặt `PRAGMA user_version = 1` (kèm khung `switch (version)` để migrate ở layer sau bằng `ALTER TABLE ADD COLUMN` cho tag/màu/region).
- `GetForOwner`: `SELECT ... WHERE owner_key=$k`.

### NotesViewModel (lớp riêng, test được)

Ctor: `NotesViewModel(INoteStore store, Func<int?> currentPageIndex, Action<int> jumpToPageIndex)`.
- Trạng thái: `_ownerKey` (string?, **chốt lúc LoadFor**), `_editingId` (string?).
- `ObservableCollection<Note> Items` — nguồn sự thật, **cập nhật tại chỗ** (add/replace/remove theo Id), KHÔNG reload sau mỗi mutate.
- `ICollectionView NotesView` — view bind ra UI: lọc theo `FilterText` (predicate `MatchesFilter`, OrdinalIgnoreCase contains trên Content) + sắp `PageIndex` tăng dần rồi `CreatedAtUnixMs` giảm dần.
- `[ObservableProperty] string Draft`; `[ObservableProperty] bool IsEditing`; `[ObservableProperty] string FilterText`; `[ObservableProperty] bool CanAddNote`.
- `partial void OnFilterTextChanged(...)` → `NotesView.Refresh()`.
- `void LoadFor(string? ownerKey)`: `CancelEdit()`; `_ownerKey = ownerKey`; `CanAddNote = ownerKey != null`; `Items.Clear()`; nếu `ownerKey != null` thêm tất cả `store.GetForOwner(ownerKey)` vào `Items`.
- `static bool MatchesFilter(Note n, string filter)`: filter rỗng → true; else `n.Content` chứa `filter` (OrdinalIgnoreCase).
- Lệnh:
  - `Save`: trim Draft; rỗng → return; `_ownerKey == null` → return; dài quá `MaxNoteLength = 20000` → đặt thông báo + return. `now = ...UtcNow ms`. Nếu `_editingId == null`: tạo `Note(Guid, _ownerKey, _ownerKey, currentPageIndex(), content, now, now)`; `store.Add(note)`; `Items.Add(note)`. Else: `rows = store.Update(_editingId, content, now)`; `rows == 0` → `LoadFor(_ownerKey)` (note đã bị xóa nơi khác) ; else thay item trong `Items` theo Id bằng record mới. Cuối: `Draft=""`, `CancelEdit()`.
  - `BeginEdit(Note note)`: `Draft = note.Content`; `_editingId = note.Id`; `IsEditing = true`.
  - `CancelEdit`: `Draft=""`; `_editingId=null`; `IsEditing=false`.
  - `Delete(Note note)`: nếu `_editingId == note.Id` → `CancelEdit()`; `store.Delete(note.Id)`; xóa khỏi `Items` theo Id.
  - `Open(Note note)`: `note.PageIndex` có giá trị → `jumpToPageIndex(note.PageIndex.Value)`.

### Nối MainViewModel

- Ctor: dựng `SqliteNoteStore(Path.Combine(AppDir(),"notes.db"))` + `EnsureSchema()`; `Notes = new NotesViewModel(noteStore, () => _documentId is null ? (int?)null : CurrentPage - 1, idx => CurrentPage = idx + 1);`. Tham số ctor tùy chọn `INoteStore? noteStore = null` để test inject.
- `public NotesViewModel Notes { get; }`.
- `LoadActiveDocument` nhánh thành công: `Notes.LoadFor(_documentId)`; nhánh catch (`_documentId=null`): `Notes.LoadFor(null)`.
- (Không cần state tab ở VM — `TabControl` tự quản lý selection.)

### XAML (MainWindow.xaml)

Trong Card sidebar phải (giữ nguyên `Grid.Column`, Visibility theo `ShowLibrary`, width binding), thay nội dung Card bằng `TabControl` style `MaterialDesignTabControl` với 2 `TabItem`:
- TabItem **Chat**: chuyển toàn bộ UI chat hiện có vào đây (nội dung là phần tử trực tiếp, không DataTemplate).
- TabItem **Notes**:
  - Ô lọc: TextBox bind `Notes.FilterText`.
  - Composer: TextBox bind `Notes.Draft` (`AcceptsReturn=True`; `KeyBinding Ctrl+Enter` → `Notes.SaveCommand`; gợi ý "Ctrl+Enter để lưu"); IsEnabled bind `Notes.CanAddNote`; nút Lưu; nút Hủy hiện khi `Notes.IsEditing`.
  - Danh sách: `ItemsControl ItemsSource="{Binding Notes.NotesView}"`. Mỗi thẻ: nội dung, badge "Trang {PageIndex+1}" nếu có anchor, icon sửa/xóa. Lệnh trong DataTemplate bind qua `RelativeSource AncestorType=ItemsControl` → `DataContext.Notes.{BeginEdit|Delete|Open}Command`, `CommandParameter="{Binding}"` (đúng pattern repo đang dùng).

## Xử lý lỗi / edge

- Chưa mở sách: `CanAddNote=false` → composer khóa; `Items` rỗng.
- Draft rỗng/whitespace: không lưu.
- Quá dài (>20000 ký tự): không lưu, báo nhẹ.
- Xóa note đang sửa: hủy chế độ sửa trước.
- Update trúng 0 dòng (note đã bị xóa): nạp lại danh sách của owner.
- Đổi sách giữa lúc đang sửa: `LoadFor` gọi `CancelEdit` trước → không ghi nhầm sách (ownerKey chốt lúc LoadFor, không đọc callback "sống").
- Lỗi store: store để lỗi nổi lên (test bắt). Lời gọi store trong VM bọc try/catch nuốt lỗi để không làm hỏng đọc (nhất quán chat/notes hiện có).

## Kiểm thử

**`SqliteNoteStore`** (store thật, file tạm):
- `Add` rồi `GetForOwner` thấy đúng note; cô lập theo `owner_key`.
- `Update` đổi `content` + `updated_at`, trả 1; update id không tồn tại trả 0.
- `Delete` xóa, trả 1; delete id không tồn tại trả 0.
- `page_index`/`document_id` null và có giá trị round-trip.
- `EnsureSchema` gọi 2 lần không lỗi; `user_version` = 1.

**`NotesViewModel`** (fake `INoteStore` in-memory + lambda, không cần PDF):
- `Save` (chưa sửa) thêm note vào `Items` với `OwnerKey` đúng + `PageIndex` = trang hiện tại (lambda trả 5 → note.PageIndex 5).
- `Save` rỗng/whitespace → không thêm.
- `Save` khi chưa mở sách (`LoadFor(null)`) → không thêm.
- `Save` khi đang sửa → cập nhật item tại chỗ (cùng Id, nội dung mới), thoát chế độ sửa.
- `BeginEdit` đặt `Draft` + `IsEditing`; `CancelEdit` xóa.
- `Delete` note đang sửa → hủy sửa + xóa khỏi `Items`.
- `Open` note có `PageIndex` → gọi lambda jump với đúng index; note không anchor → không gọi.
- `LoadFor(id)` nạp đúng danh sách + `CanAddNote=true`; `LoadFor(null)` làm rỗng + `CanAddNote=false`.
- `MatchesFilter`: rỗng → true; chứa (không phân biệt hoa thường) → true; không chứa → false.

**Verify GUI thủ công:** chuyển tab Chat/Notes — **kiểm tra cuộn chat + focus có bị reset không** (nếu có và khó chịu → áp fix keep-alive template, ghi nhận); nội dung/stream chat phải còn nguyên; tạo/sửa/xóa note không nhảy scroll danh sách note; lọc; click thẻ nhảy trang; composer khóa khi ở thư viện; Ctrl+Enter lưu.

## Loại trừ (Layer sau)

- Neo theo vùng chọn text / highlight tô màu (Layer 2).
- Tag/màu/tìm full-text/xuất (Layer 5) — schema đã chừa đường qua `user_version` + `ALTER TABLE ADD COLUMN`.
- Phạm vi workspace (Layer 3) — đã chừa qua `owner_key`.
- Định danh tài liệu ổn định + nối note qua các phiên bản file (chờ tính năng Save/Export PDF) — ghi nhận ở epic.
