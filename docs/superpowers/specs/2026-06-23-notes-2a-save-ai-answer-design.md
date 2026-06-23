# Notes Layer 2a — Lưu câu trả lời AI thành note (one-click) — Thiết kế

**Ngày:** 2026-06-23 (revise sau phản hồi UX: bỏ composer, dùng one-click)
**Trạng thái:** Đã duyệt hướng, chờ review spec.
**Thuộc epic:** `docs/superpowers/specs/2026-06-22-taking-note-epic.md` (Layer 2, sub-project 2a). Tiền đề: Notes Layer 1 (PR #14) + 2b (PR #15). Thay thế cách làm composer ở bản nháp trước trên cùng nhánh `feature/notes-2a-save-ai-answer`.

## Mục tiêu

Bấm một nút trên tin nhắn AI là **lưu thẳng câu trả lời thành note** — không chuyển tab, không bước viết bình luận. Note tích lũy thành kho tri thức (về sau làm nguồn cho AI: tạo slide, tóm tắt... — đã ghi backlog Layer 4).

## Vì sao đổi so với bản nháp trước

Bản nháp 2a dùng luồng composer (mở tab Notes, câu trả lời AI vào banner trích dẫn, người dùng gõ bình luận rồi lưu). Phản hồi: (1) chuyển tab + banner cắt chữ nên không thấy rõ câu trả lời; (2) nút chữ trên bong bóng gây rối; (3) **không cần viết bình luận** — bản thân câu trả lời là note. Vậy chuyển sang **one-click**.

## Phạm vi

- Chỉ đổi luồng lưu câu trả lời AI thành one-click + xác nhận snackbar + nút gọn (hover mới hiện).
- Giữ nguyên 2b (chọn text → composer + trích dẫn neo trang) và toàn bộ tab Notes/thẻ note.
- KHÔNG làm: note làm nguồn cho AI (backlog Layer 4), citations (backlog Layer 4), neo trang cho note AI.

## Bối cảnh code

- `ChatMessage { Role, Content }`; `ChatMessages` ObservableCollection trong `MainViewModel`. Câu trả lời AI ở `Content` (Role == "AI").
- `NotesViewModel` (Layer 1/2b): `Items`, `Draft`, `Save` (Draft-driven), `_all`, `InsertSorted`, `MatchesFilter`, `_ownerKey` (chốt khi `LoadFor`), `BeginNoteFromSelection` (2b, composer). Bản nháp trước đã thêm `BeginNoteFromText`/nullable `_pendingPageIndex` + `MainViewModel.SaveAnswerAsNote` (composer) — sẽ THAY bằng one-click.
- Thẻ note hiện `Quote` (nếu có) + `Content`; note AI one-click sẽ đặt câu trả lời vào `Content` (không Quote, không trang).
- MaterialDesignThemes có `SnackbarMessageQueue` + control `materialDesign:Snackbar`.

## Kiến trúc & thành phần

### NotesViewModel — thêm tạo note trực tiếp

```csharp
// Tạo note trực tiếp (không qua Draft/composer). Dùng cho one-click lưu câu trả lời AI.
// Trả về true nếu đã lưu (có sách đang mở). content rỗng -> không lưu.
public bool AddNote(string content, string? quote, int? pageIndex)
{
    if (_ownerKey == null) return false;
    string text = (content ?? string.Empty).Trim();
    if (text.Length == 0) return false;
    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey, pageIndex, quote, text, now, now);
    try { _store.Add(note); } catch { return false; }
    _all.Add(note);
    if (MatchesFilter(note, FilterText)) InsertSorted(note);
    return true;
}
```

Dọn bản nháp trước: bỏ `BeginNoteFromText` và đưa `BeginNoteFromSelection` về thân cũ (inline), `_pendingPageIndex` về `int` (chỉ 2b dùng, luôn có trang). (Nếu để lại cũng không sai, nhưng tránh code chết.)

### MainViewModel — command + snackbar

```csharp
public MaterialDesignThemes.Wpf.SnackbarMessageQueue NotesSnackbar { get; } = new();

[RelayCommand]
private void SaveAnswerAsNote(ChatMessage? msg)
{
    if (msg is null || msg.Role != "AI" || string.IsNullOrWhiteSpace(msg.Content)) return;
    bool saved = Notes.AddNote(msg.Content, null, null);
    NotesSnackbar.Enqueue(saved ? "Đã lưu vào ghi chú" : "Hãy mở một tài liệu để lưu ghi chú");
}
```

### MainWindow.xaml

- **Nút gọn, hover mới hiện:** trong DataTemplate bong bóng chat, nút **icon-only** (`PackIcon NotePlusOutline`, có ToolTip "Lưu thành ghi chú"), mặc định ẩn; chỉ hiện khi vừa là tin nhắn AI vừa đang rê chuột vào bong bóng — `MultiDataTrigger` [ `Role == "AI"`, `IsMouseOver` của `materialDesign:Card` (RelativeSource AncestorType=Card) == True ] → `Visibility=Visible`. Lệnh bind qua `RelativeSource AncestorType=ItemsControl` → `DataContext.SaveAnswerAsNoteCommand`, `CommandParameter={Binding}`.
- **Snackbar:** thêm `<materialDesign:Snackbar MessageQueue="{Binding NotesSnackbar}"/>` đặt overlay đáy vùng chat (hoặc đáy cửa sổ), để báo "Đã lưu vào ghi chú".
- Bỏ nút chữ + DataTrigger đơn của bản nháp trước.

## Xử lý lỗi / edge

- Chưa mở sách (`_ownerKey == null`): `AddNote` trả false → snackbar "Hãy mở một tài liệu...". (Thực tế đang chat thì đã mở sách.)
- Tin nhắn chào / Content rỗng / không phải AI: command bỏ qua (nút cũng không hiện vì chỉ AND khi Role==AI).
- Lưu trùng (bấm nhiều lần): mỗi lần tạo một note mới — chấp nhận (người dùng tự xóa nếu thừa). Không khử trùng ở v1.

## Kiểm thử

- `NotesViewModel.AddNote` (fake store + LoadFor): tạo note với `Content` đúng, `Quote`/`PageIndex` null, thêm vào `Items`, trả true; `AddNote` khi chưa LoadFor (ownerKey null) trả false + không thêm; content rỗng/whitespace trả false. Lọc: note mới chỉ vào `Items` nếu khớp FilterText.
- 2b (`BeginNoteFromSelection`) vẫn xanh sau khi dọn.
- `MainViewModel.SaveAnswerAsNote`: glue (guard non-AI/rỗng/null) — verify chính ở GUI vì cần sách mở; snackbar/nút hover verify GUI.

## Manual GUI verify

1. Rê chuột vào bong bóng AI → hiện icon "Lưu thành ghi chú" (gọn); bong bóng User không có.
2. Bấm → KHÔNG chuyển tab; snackbar "Đã lưu vào ghi chú" hiện; mở tab Notes thấy note mới = câu trả lời AI (không badge trang).
3. Bấm nhiều lần tạo nhiều note (chấp nhận).
4. Không hỏng: 2b chọn text → note trích dẫn vẫn neo trang; lọc/sửa/xóa note vẫn chạy.

## Loại trừ (backlog)

- Note làm nguồn cho AI tạo slide/tóm tắt (Layer 4) · citations (Layer 4) · neo trang cho note AI · khử trùng note.
