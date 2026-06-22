# Notes Layer 2b — Chọn text → note trích dẫn — Thiết kế

**Ngày:** 2026-06-22
**Trạng thái:** Đã duyệt thiết kế, chờ review spec.
**Thuộc epic:** `docs/superpowers/specs/2026-06-22-taking-note-epic.md` (Layer 2, sub-project 2b). Tiền đề: Notes Layer 1 (PR #14).

## Mục tiêu

Người đọc bôi đen một đoạn text trên trang (theo dòng chữ), bấm nút nổi "Thêm ghi chú" → mở tab Notes với ô soạn đã gắn sẵn đoạn trích dẫn (lưu vào trường quote riêng) và neo vào trang đang xem; viết bình luận rồi lưu.

## Phạm vi

- Chọn trong **một trang** (chọn vắt nhiều trang: để sau).
- Giữ nguyên: pan/scroll (PagesScrollViewer), double-click ghost, Ctrl+wheel zoom. Left-drag hiện trống → dùng cho chọn text.
- KHÔNG đụng các store/feature khác (chat, library) — chỉ thêm cột `quote` vào bảng `note`.

## Bối cảnh code (đã khảo sát)

- Chưa có cơ chế kéo-chọn text; mouse trên InteractionCanvas mới có double-click hit-test GhostText (`PdfViewerControl.xaml.cs` OnCanvasMouseDown/HandleDoubleClick), Ctrl+wheel zoom, lật trang.
- `PdfObjectManager.MapPage` dựng `GhostText { PageIndex, CharIndex, Text, Bounds }` từng ký tự (thứ tự đọc theo CharIndex); `HitTest(point)` trả ký tự tại điểm.
- `_slots` (PageSlot: PageIndex,X,Y,W,H theo DIP) + chuyển screen↔PDF: `pdfX=(p.X-rect.Left)/scale`.
- Vẽ highlight đã có: `DrawHighlights` dùng `PdfCoordinateMapper.PdfPointToRender` (dpi=72) → `SKCanvas.DrawRect` trên overlay.
- `Note(Id, OwnerKey, DocumentId, PageIndex, Content, CreatedAtUnixMs, UpdatedAtUnixMs)`; `SqliteNoteStore` có `PRAGMA user_version` (hiện = 1). `NotesViewModel(INoteStore, Func<int?> currentPageIndex, Action<int> jumpToPageIndex)` với composer Draft + Save.
- Sidebar phải là `TabControl` MaterialDesign (Chat | Notes) — hiện không có state chọn tab ở VM.

## Kiến trúc & thành phần

### A. Logic chọn (thuần, test được) — `Core/TextSelectionResolver.cs`

Tách phần tính toán khỏi view để unit-test. Đầu vào là danh sách ký tự đã ánh xạ của MỘT trang + 2 chỉ số anchor/focus.

```csharp
public readonly record struct SelChar(int CharIndex, string Text, System.Windows.Rect Bounds);
public sealed record SelectionResult(string Text, IReadOnlyList<System.Windows.Rect> LineRects);

public static class TextSelectionResolver
{
    // chars: các ký tự của trang (CharIndex tăng theo thứ tự đọc). anchorIndex/focusIndex: CharIndex.
    // Trả: chuỗi text trong dải [min,max] (đảo chiều vẫn đúng) + danh sách rect đã gộp theo dòng.
    public static SelectionResult Resolve(IReadOnlyList<SelChar> chars, int anchorIndex, int focusIndex);
}
```
- Dải bao gồm cả hai đầu; anchor>focus thì hoán đổi.
- Nối Text theo thứ tự CharIndex. Gộp rect theo dòng: gom các ký tự cùng dòng (chênh lệch tâm-Y nhỏ hơn nửa chiều cao ký tự) thành một rect bao (union) để highlight gọn.

### B. Chọn trên canvas (`PdfViewerControl.xaml(.cs)`)

- Bảo đảm `PdfObjectManager.MapPage` cho trang đang tương tác (đã dùng cho double-click).
- Handler trên InteractionCanvas:
  - `MouseLeftButtonDown`: tìm slot trang chứa điểm; tìm ký tự gần nhất (HitTest, nếu null thì min khoảng cách tâm) → lưu `(_selPageIndex, _anchorCharIndex)`; `CaptureMouse()`; xóa selection cũ.
  - `MouseMove` (khi đang giữ trái): chỉ xét cùng `_selPageIndex`; ký tự gần nhất = focus; gọi `TextSelectionResolver.Resolve` → lưu `_selectionRects` (PDF coords) + `_selectionText`; `InvalidateVisual()` để vẽ overlay chọn.
  - `MouseLeftButtonUp`: `ReleaseMouseCapture()`; nếu selection rỗng → bỏ; nếu có → đặt vị trí + hiện nút nổi "Thêm ghi chú" (canvas coords, gần cuối vùng chọn).
  - Esc / MouseLeftButtonDown mới / đổi trang → `ClearSelection()` (xóa rect + ẩn nút + InvalidateVisual).
- Vẽ: trong hàm render overlay, ngoài search-highlight, vẽ `_selectionRects` bằng paint màu xanh mờ (ví dụ ARGB 80,33,150,243) qua đúng `PdfCoordinateMapper` như DrawHighlights.
- Nút nổi: một `Button` trong InteractionCanvas (ẩn mặc định), đặt `Canvas.Left/Top` theo vị trí; Click → execute DP command.
- DependencyProperty mới: `public ICommand AddNoteFromSelectionCommand { get; set; }` (DP); khi bấm nút nổi, execute với tham số `new NoteSelection(_selectionText, _selPageIndex)` rồi `ClearSelection()`.
- Kiểu chia sẻ: `public sealed record NoteSelection(string Quote, int PageIndex);` (đặt ở `Models/`).

### C. Model + store

- `Note` thêm trường: `Note(string Id, string OwnerKey, string? DocumentId, int? PageIndex, string? Quote, string Content, long CreatedAtUnixMs, long UpdatedAtUnixMs)`.
- `SqliteNoteStore`:
  - `SchemaVersion = 2`. `EnsureSchema`: sau CREATE, nếu `user_version < 2` thì `ALTER TABLE note ADD COLUMN quote TEXT;` rồi set `user_version = 2`. (CREATE TABLE cho db mới không có `quote` trong câu CREATE gốc; migration thêm vào — hoặc CREATE mới gồm luôn `quote` và ALTER chỉ chạy cho db cũ. Chọn: CREATE gồm `quote`, ALTER trong nhánh migrate dùng `PRAGMA table_info` để chỉ thêm khi thiếu, tránh lỗi cột trùng.)
  - `Add`: chèn cả `quote`. `GetForOwner`: đọc `quote`. `Update(id, content, now)`: giữ nguyên (không đụng quote).

### D. NotesViewModel

- Thêm: `[ObservableProperty] int _rightTabIndex;` (0=Chat,1=Notes; bind `TabControl.SelectedIndex`); `[ObservableProperty] string? _pendingQuote;` (hiện banner trích dẫn trên ô soạn); field `_pendingPageIndex`.
- `void BeginNoteFromSelection(string quote, int pageIndex)`: `CancelEdit()`; `_pendingQuote = quote`; `_pendingPageIndex = pageIndex`; `RightTabIndex = 1`.
- `Save`: cho lưu nếu `Draft` không rỗng **hoặc** có `_pendingQuote`. Khi tạo mới: `Note(..., PageIndex = _pendingQuote != null ? _pendingPageIndex : currentPageIndex(), Quote = _pendingQuote, Content = Draft.Trim(), ...)`. Sau lưu: xóa `_pendingQuote`/`_pendingPageIndex`. (Đường sửa note cũ giữ nguyên, không gắn quote.)
- `CancelEdit`/`LoadFor`: đồng thời xóa `_pendingQuote`/`_pendingPageIndex`.

### E. MainViewModel + XAML

- `MainViewModel`: `[RelayCommand] void AddNoteFromSelection(NoteSelection? sel)` → nếu sel != null gọi `Notes.BeginNoteFromSelection(sel.Quote, sel.PageIndex)`.
- `MainWindow.xaml`:
  - Bind `PdfViewer.AddNoteFromSelectionCommand` → `AddNoteFromSelectionCommand`.
  - `TabControl SelectedIndex="{Binding Notes.RightTabIndex, Mode=TwoWay}"`.
  - Trong ô soạn tab Notes: banner trích dẫn (TextBlock viền trái, `Text="{Binding Notes.PendingQuote}"`, `Visibility` ẩn khi null qua `NullToCollapsedConverter` đã có).
  - Thẻ note: thêm khối Quote (viền trái, in nghiêng) phía trên Content, ẩn nếu Quote null.

## Xử lý lỗi / edge

- Selection rỗng (click không kéo) → không hiện nút.
- Bấm "Thêm ghi chú" khi chưa mở sách: `BeginNoteFromSelection` vẫn set Pending, nhưng `Save` chặn nếu `_ownerKey == null` (như Layer 1) → thực tế đang đọc sách nên luôn có owner.
- Đổi trang / cuộn khi đang chọn → xóa selection.
- Lỗi store khi Save: nuốt như Layer 1.

## Kiểm thử

- `TextSelectionResolver` (thuần): dải thuận/đảo chiều cho cùng kết quả; nối text đúng thứ tự đọc; 1 ký tự; gộp rect 2 dòng thành 2 rect; chọn rỗng (anchor==focus hợp lệ = 1 ký tự).
- `SqliteNoteStore`: quote round-trip (Add có quote → GetForOwner thấy); `Update` không xóa quote; **migrate**: tạo bảng kiểu v1 (không có cột quote, user_version chưa set) rồi `EnsureSchema` → cột quote xuất hiện, user_version=2, dữ liệu cũ còn nguyên.
- `NotesViewModel`: `BeginNoteFromSelection` set PendingQuote + RightTabIndex=1; `Save` tạo note có Quote + đúng PageIndex (dùng _pendingPageIndex, không phải currentPageIndex); `Save` với Draft rỗng nhưng có quote → vẫn tạo; sau lưu PendingQuote=null; `CancelEdit`/`LoadFor` xóa pending.
- GUI thủ công: kéo chọn theo dòng, overlay xanh, nút nổi, bấm → tab Notes + banner trích dẫn; lưu → thẻ có quote + click nhảy trang; pan/zoom/double-click không hỏng.

## Loại trừ (Layer/sub-project sau)

- Chọn vắt nhiều trang; copy clipboard; sửa/bỏ quote của note đã tạo.
- Lưu + vẽ lại highlight tô màu trên trang (2c) — 2b chỉ highlight tạm trong lúc chọn, không lưu rect.
