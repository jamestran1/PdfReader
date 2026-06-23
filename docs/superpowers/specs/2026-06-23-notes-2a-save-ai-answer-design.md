# Notes Layer 2a — Lưu câu trả lời AI thành note — Thiết kế

**Ngày:** 2026-06-23
**Trạng thái:** Đã duyệt thiết kế, chờ review spec.
**Thuộc epic:** `docs/superpowers/specs/2026-06-22-taking-note-epic.md` (Layer 2, sub-project 2a). Tiền đề: Notes Layer 1 (PR #14) + 2b (PR #15).

## Mục tiêu

Mỗi tin nhắn AI trong khung chat có nút "Lưu thành ghi chú". Bấm → mở tab Notes, câu trả lời AI vào banner "Trích dẫn" (trường Quote), người dùng thêm bình luận rồi lưu. Note KHÔNG neo trang (câu trả lời AI thường dựa trên nhiều chỗ).

## Phạm vi

- Chỉ tái dùng luồng "pending quote" của 2b, mở rộng để trang neo có thể null.
- KHÔNG làm citations/nguồn (mỗi đoạn trả lời gắn trang+đoạn, click highlight nguồn) — đã ghi backlog Layer 4.
- KHÔNG neo trang cho note tạo từ câu trả lời AI.

## Bối cảnh code (đã biết)

- `ChatMessage { Role, Content, Timestamp }` (ViewModels/MainViewModel.cs); `ChatMessages` là `ObservableCollection<ChatMessage>`, mỗi message truy cập được. Câu trả lời AI nằm ở `Content` (Role == "AI").
- 2b đã có: `NotesViewModel.BeginNoteFromSelection(string quote, int pageIndex)` set `PendingQuote` + `_pendingPageIndex` (int) + `RightTabIndex = 1`; `Save` dùng `_pendingPageIndex` khi có pending quote. Banner "Trích dẫn" trên ô soạn + khối quote trên thẻ note đã có.
- Chat ItemsControl + DataTemplate bong bóng ở `MainWindow.xaml`; có `RoleToBrushConverter`/`RoleToAlignConverter` cho bong bóng theo Role.

## Kiến trúc & thành phần

### NotesViewModel (tổng quát hóa neo trang null)

- Đổi `private int _pendingPageIndex;` → `private int? _pendingPageIndex;`.
- Thêm:
  ```csharp
  // Bắt đầu tạo note từ một đoạn text bất kỳ (vùng chọn trang hoặc câu trả lời AI).
  // pageIndex = null nghĩa là không neo trang.
  public void BeginNoteFromText(string quote, int? pageIndex)
  {
      CancelEdit();
      PendingQuote = quote;
      _pendingPageIndex = pageIndex;
      RightTabIndex = 1;
  }
  ```
- `BeginNoteFromSelection(string quote, int pageIndex)` trở thành wrapper: `=> BeginNoteFromText(quote, pageIndex);` (hành vi 2b không đổi).
- `Save` (nhánh thêm mới): `int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();` — `_pendingPageIndex` giờ là `int?`, note từ AI có `null` → PageIndex null. Phần còn lại giữ nguyên (cho lưu khi Draft rỗng nếu có quote; xóa pending sau lưu).

### MainViewModel

```csharp
[RelayCommand]
private void SaveAnswerAsNote(ChatMessage? msg)
{
    if (msg is null || msg.Role != "AI" || string.IsNullOrWhiteSpace(msg.Content)) return;
    Notes.BeginNoteFromText(msg.Content, null);
}
```

### MainWindow.xaml

- Trong `DataTemplate` của bong bóng chat, thêm nút icon "Lưu thành ghi chú" (PackIcon `NotePlusOutline` hoặc `ContentSave`), tooltip "Lưu thành ghi chú".
- Nút chỉ hiện trên tin nhắn AI: `Visibility` qua DataTrigger `Role == "AI"` (mặc định Collapsed, Trigger → Visible). (Không thêm converter mới; dùng Style + DataTrigger.)
- Bind lệnh qua tổ tiên ItemsControl:
  ```xml
  Command="{Binding DataContext.SaveAnswerAsNoteCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
  CommandParameter="{Binding}"
  ```
- Bấm → `SaveAnswerAsNote` → mở tab Notes, banner "Trích dẫn" (đã có) hiện câu trả lời AI; người dùng gõ bình luận, Ctrl+Enter lưu.

## Xử lý lỗi / edge

- Tin nhắn chào đầu ("Xin chào...") cũng Role == "AI" nên sẽ có nút; bấm tạo note với lời chào làm trích dẫn — chấp nhận được (hiếm khi bấm), không thêm cờ phân biệt.
- Bấm khi chưa mở sách: `Save` vẫn chặn nếu `_ownerKey == null` (như Layer 1) → không tạo note rác.
- Câu trả lời AI rỗng/đang stream: lệnh chặn khi Content rỗng.

## Kiểm thử

- `NotesViewModel`: `BeginNoteFromText(text, null)` → `PendingQuote == text`, `RightTabIndex == 1`; `Save` (sau LoadFor + Draft = "bình luận") tạo note `Quote == text`, `PageIndex == null`, `Content == "bình luận"`; `Save` chỉ-quote Draft rỗng vẫn tạo (PageIndex null). Test 2b cũ (`BeginNoteFromSelection` neo trang) vẫn xanh.
- `MainViewModel`: `SaveAnswerAsNote(null)` / Role "User" / Content rỗng → không gọi (PendingQuote vẫn null); AI hợp lệ → PendingQuote = Content, RightTabIndex = 1.
- GUI thủ công: nút chỉ hiện trên bong bóng AI; bấm → tab Notes + banner; lưu → thẻ có quote (cắt ~3 dòng), không badge trang.

## Loại trừ (đã ghi backlog)

- Citations/nguồn theo đoạn + highlight nguồn trên trang (Layer 4).
- Neo trang cho note AI.
