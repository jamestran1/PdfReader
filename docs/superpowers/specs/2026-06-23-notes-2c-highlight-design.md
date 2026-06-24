# Notes Layer 2c — Highlight tô màu (lưu + vẽ lại trên trang) — Thiết kế

**Ngày:** 2026-06-23
**Trạng thái:** Đã duyệt hướng, chờ review spec.
**Thuộc epic:** `docs/superpowers/specs/2026-06-22-taking-note-epic.md` (Layer 2, sub-project 2c). Tiền đề: Layer 1 (#14), 2b chọn-text (#15), 2a (#16). Khép Layer 2.

## Mục tiêu

Note tạo từ chọn-text (2b) để lại một **highlight vàng** trên trang; highlight được **lưu** và **vẽ lại** mỗi khi mở/hiển thị trang đó (đọc tới đâu thấy dấu tới đó). Xóa note → highlight biến mất.

## Quyết định (từ brainstorm)

1. **Model A:** highlight = phần hiển thị trên trang của note chọn-text. Không có thực thể highlight riêng. "Chỉ tô màu không ghi chú" = note chỉ-trích-dẫn (đã cho phép ở 2b).
2. **Màu:** một màu vàng mặc định (`#FFEB3B`), vẽ mờ. Vẫn lưu cột màu để sau mở palette.
3. **Click trên trang:** v1 KHÔNG xử lý (chỉ hiển thị). Click-highlight→note đã đẩy issue #30.

## Phạm vi

- Chỉ note chọn-text (có rects + trang) mới vẽ highlight. Note AI (2a) / tự-do: `Rects=null` → không vẽ.
- Quản lý (xem/xóa) qua tab Notes như cũ; xóa note → highlight mất.
- KHÔNG: click highlight (#30), palette nhiều màu, markup bút/hình (#21).

## Bối cảnh code (đã có)

- `Note(Id, OwnerKey, DocumentId, PageIndex, Quote, Content, CreatedAtUnixMs, UpdatedAtUnixMs)`; `SqliteNoteStore` user_version = 2.
- 2b: `PdfViewerControl` có `_selectionRectsPdf` (List<Rect> per-line, **tọa độ PDF top-origin**) lúc chọn; raise `NoteSelection(Quote, PageIndex)` qua DP command `AddNoteFromSelectionCommand` → `MainViewModel.AddNoteFromSelection` → `Notes.BeginNoteFromSelection(quote, pageIndex)` → composer Save tạo note.
- `PdfViewerControl` vẽ overlay theo từng page slot: `DrawHighlights` (search, dùng `MatchRect` bottom-origin) + `DrawSelectionOverlay` (vùng chọn, top-origin, KHÔNG lật Y — đã fix). `_slots` (PageSlot), `ZoomLevel`, `_currentDocument`.
- `TextSelectionResolver.Resolve` trả rects gộp theo dòng (đã ổn định, top-origin).

## Kiến trúc & thành phần

### A. Model + store

- Thêm record `HighlightRect(double X, double Y, double W, double H)` (Models) — tọa độ PDF top-origin của một dải dòng.
- `Note` thêm: `IReadOnlyList<HighlightRect>? Rects` và `string? Color` (chèn sau `Quote`, hoặc cuối — vị trí cố định, cập nhật mọi nơi tạo Note).
- `SqliteNoteStore` user_version **3**: `ALTER TABLE note ADD COLUMN rects TEXT` + `ADD COLUMN color TEXT` (chỉ thêm khi thiếu, qua `PRAGMA table_info` như migrate trước). `Add`: serialize `Rects` → JSON (System.Text.Json) vào cột `rects`, ghi `color`. `GetForOwner`: deserialize `rects` (null nếu cột null/rỗng) + đọc `color`.

### B. Luồng chọn → rects → note

- `NoteSelection` thêm `IReadOnlyList<HighlightRect> Rects` (viewer dựng từ `_selectionRectsPdf` lúc bấm "Thêm ghi chú").
- `MainViewModel.AddNoteFromSelection(NoteSelection sel)` → `Notes.BeginNoteFromSelection(sel.Quote, sel.PageIndex, sel.Rects)`.
- `NotesViewModel`: `BeginNoteFromSelection(quote, pageIndex, rects)` giữ thêm `_pendingRects`. `Save` (nhánh tạo mới, khi `hasQuote`): gắn `Rects = _pendingRects`, `Color = DefaultHighlightColor ("#FFEB3B")`. (Note không có pending rects → `Rects=null`, `Color=null`.)

### C. Tập highlight cho viewer

- `NotesViewModel` thêm `ObservableCollection<Note> Highlights` = các note (trong sách hiện tại) có `Rects != null && Rects.Count > 0`, BẤT KỂ filter (highlight luôn hiện trên trang). Cập nhật: `LoadFor` (rebuild), `AddNote`/`Save`-tạo-mới (thêm nếu có rects), `Delete` (gỡ), `Save`-edit (nội dung đổi, rects giữ → cập nhật tại chỗ nếu cần). 
- `PdfViewerControl` thêm DependencyProperty `Highlights` (IEnumerable, bind `Notes.Highlights`). Khi collection đổi (CollectionChanged) hoặc DP set → `InvalidateVisual` (repaint overlay).

### D. Vẽ lại trên trang

- Thêm `DrawSavedHighlights(SKCanvas canvas, int pageIndex, Rect pageRect, float scale)` trong `PdfViewerControl`, gọi trong vòng vẽ overlay cho mỗi slot (cạnh `DrawHighlights`/`DrawSelectionOverlay`):
  - Lặp `Highlights` có `PageIndex == pageIndex`, mỗi `HighlightRect r`: vẽ `SKRect` tại `(pageRect.Left + r.X*scale, pageRect.Top + r.Y*scale, r.W*scale, r.H*scale)` — **top-down, không lật Y** (đúng hệ `_selectionRectsPdf`/`DrawSelectionOverlay`).
  - Màu: parse `Color` (hex) → `SKColor` với alpha mờ (vd ~70-90); fallback vàng nếu null/parse lỗi.
- Overlay đã repaint sẵn khi cuộn/zoom/đổi trang; thêm repaint khi `Highlights` đổi (mục C).

## Xử lý lỗi / edge

- Note không rects (AI/tự-do): không vào `Highlights`, không vẽ.
- `rects` JSON hỏng khi đọc: coi như null (try/catch), note vẫn dùng được, chỉ không có highlight.
- Đổi sách: `LoadFor` rebuild `Highlights`.
- Highlight chồng nhiều dòng: mỗi dòng một rect (đã gộp ở resolver) → vẽ nhiều rect liền mạch.

## Kiểm thử

- `SqliteNoteStore`: rects + color round-trip (Add note có rects → GetForOwner trả đúng list + color); note rects null round-trip; **migrate v2→v3** (db kiểu v2 không có cột rects/color → EnsureSchema thêm cột, dữ liệu cũ giữ, đọc được).
- `NotesViewModel`: `BeginNoteFromSelection(quote, page, rects)` giữ pending rects; `Save` tạo note có `Rects` + `Color=#FFEB3B`; note vào `Highlights`. `AddNote`/note AI (no rects) KHÔNG vào `Highlights`. `Delete` gỡ khỏi `Highlights`. `LoadFor` rebuild `Highlights` (chỉ note có rects). Highlights bỏ qua FilterText (vẫn đủ khi đang lọc).
- Flow rects: `NoteSelection.Rects` → `_pendingRects` → note.Rects.
- Vẽ overlay + DP repaint khi Highlights đổi: verify GUI.

## Manual GUI verify

1. Chọn text → "Thêm ghi chú" → lưu (có/không bình luận): trên trang xuất hiện highlight vàng đúng đoạn.
2. Cuộn đi rồi quay lại / zoom in-out / đóng-mở sách: highlight vẫn ở đúng chỗ.
3. Xóa note ở tab Notes → highlight biến mất.
4. Note AI (2a) không tạo highlight.
5. Không hỏng: vùng-chọn tạm khi đang kéo (2b) vẫn hiện; search highlight vẫn đúng.

## Loại trừ (GitHub Issues)

- Click highlight → mở note (#30) · palette màu · markup bút/hình (#21).
