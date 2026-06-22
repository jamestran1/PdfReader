# Lưu lịch sử chat theo từng quyển sách — Thiết kế

**Ngày:** 2026-06-22
**Trạng thái:** Đã duyệt thiết kế, chờ review spec

## Mục tiêu

Mỗi quyển sách trong thư viện có một luồng hội thoại chat riêng được lưu bền vững. Khi mở lại sách, các tin nhắn cũ hiện lại trong khung chat VÀ AI "nhớ tiếp mạch" hội thoại (trả lời câu mới dựa trên cả các lượt trước).

## Bối cảnh hiện trạng

- `MainViewModel.ChatMessages` (`ObservableCollection<ChatMessage>`) là các bong bóng hiển thị, chỉ nằm trong RAM. Constructor seed 1 lời chào AI.
- `AiChatService._history` (`List<ChatMessage>` của Microsoft.Extensions.AI) là lịch sử gửi cho LLM. Mỗi lượt User nhúng nguyên khối "Ngữ cảnh tài liệu:\n{context}\n\nCâu hỏi: {question}". `ResetConversation()` xóa về chỉ còn System prompt.
- Khi mở/đổi sách, `LoadActiveDocument` gọi `_chatService.ResetConversation()` nhưng KHÔNG xóa `ChatMessages` → lỗi nhẹ: đổi sách vẫn thấy bong bóng của sách cũ. Tính năng này sửa luôn lỗi đó.
- `_documentId` = SHA256 nội dung file (`DocumentId.FromFile`), là khóa chung cho library (`library.db`) và index (`index.db`). Dùng làm khóa cho lịch sử chat.
- Pattern store đã có: `ISomethingStore` + `SqliteSomethingStore`, connection per-operation với `Pooling=False` (xem `SqliteLibraryStore`).

## Quyết định thiết kế

1. **Phạm vi nhớ:** AI nhớ tiếp mạch — khôi phục cả bong bóng UI lẫn lịch sử LLM.
2. **Cơ chế lưu:** SQLite, file riêng `chats.db` trong AppDir.
3. **Nút xóa chat riêng:** chưa làm (YAGNI). Một luồng hội thoại liên tục cho mỗi sách. Lịch sử chỉ mất khi xóa sách khỏi thư viện.
4. **Nội dung lưu là sạch:** lưu câu hỏi/đáp đúng như bong bóng UI (`Role` + `Content`), KHÔNG kèm khối "Ngữ cảnh tài liệu". Khi dựng lại lịch sử LLM, ngữ cảnh tài liệu vẫn được thêm tươi cho mỗi câu hỏi mới (tránh phình token, tránh nhân đôi ngữ cảnh).

## Kiến trúc & thành phần

### Model

`ChatHistoryEntry(string DocumentId, string Role, string Content, long CreatedAtUnix)` — record bất biến, đại diện một tin nhắn đã lưu.

### Store mới

`IChatHistoryStore`:
- `void EnsureSchema()`
- `void Append(string documentId, string role, string content, long createdAtUnix)`
- `IReadOnlyList<ChatHistoryEntry> GetAll(string documentId)` — thứ tự thời gian tăng dần (theo `id`/`created_at`)
- `void DeleteForDocument(string documentId)`

`SqliteChatHistoryStore : IChatHistoryStore`:
- Connection per-operation, chuỗi kết nối có `Pooling=False` (nhất quán `SqliteLibraryStore`, tránh khóa file `chats.db`).
- Bảng: `chat_message(id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, created_at INTEGER NOT NULL)`.
- Index: `CREATE INDEX IF NOT EXISTS ix_chat_message_doc ON chat_message(document_id, id)`.
- `GetAll`: `WHERE document_id = $id ORDER BY id ASC`.

### Sửa `AiChatService`

Thêm: `void SeedHistory(IEnumerable<(string role, string content)> turns)`
- Xóa `_history`, thêm lại `System` prompt.
- Với mỗi lượt: map `role == "AI"` → `ChatRole.Assistant`, ngược lại → `ChatRole.User`. Thêm `Content` thô (KHÔNG bọc khối ngữ cảnh).
- Seed danh sách rỗng → hành vi giống `ResetConversation` (chỉ còn System prompt).

`ResetConversation()` giữ nguyên (vẫn dùng nội bộ; `SeedHistory([])` tương đương).

### Sửa `MainViewModel`

- Ctor: dựng `SqliteChatHistoryStore(Path.Combine(AppDir(), "chats.db"))`, gọi `EnsureSchema()`. Giữ ở field `_chatHistory`.
- Bỏ phần seed lời chào trong ctor — chuyển thành hàm dùng lại (xem dưới), vì lời chào giờ phụ thuộc việc sách hiện tại có lịch sử hay không. Khi khởi động chưa mở sách nào: vẫn hiện lời chào.
- Hàm private `LoadChatHistory()`:
  1. `ChatMessages.Clear()`.
  2. Nếu `_documentId` null → thêm bong bóng chào, `_chatService.ResetConversation()`, return.
  3. `var entries = _chatHistory.GetAll(_documentId)` (bọc try/catch, lỗi → coi như rỗng).
  4. Nếu rỗng → thêm bong bóng chào (không lưu DB), `_chatService.ResetConversation()`.
  5. Nếu có → thêm từng `ChatMessage { Role, Content }` vào `ChatMessages`; gọi `_chatService.SeedHistory(entries.Select(e => (e.Role, e.Content)))`.
- `LoadActiveDocument`: thay `_chatService.ResetConversation()` bằng gọi `LoadChatHistory()` (sau khi set `_documentId`). Nhánh lỗi mở file (set `_documentId = null`) cũng gọi `LoadChatHistory()` để dọn về trạng thái chào.
- `SendMessage`: sau khi lượt hoàn tất (trong khối `finally` hoặc cuối `try`, kể cả lỗi/gián đoạn), nếu `_documentId != null`, ghi 2 dòng qua store (bọc try/catch nuốt lỗi):
  - `Append(_documentId, "User", question, now)`
  - `Append(_documentId, "AI", aiMessage.Content, now)`
  - `now = DateTimeOffset.UtcNow.ToUnixTimeSeconds()`.
  - Trường hợp chưa cấu hình API key (thoát sớm sau khi thêm bong bóng "Chưa cấu hình..."): vẫn lưu cặp User + AI(thông báo) để transcript trung thực.
- `RemoveLibraryItem`: sau `_library.Remove(item)`, gọi `_chatHistory.DeleteForDocument(item.DocumentId)` (bọc try/catch).

### Xử lý lỗi

Mọi thao tác store trong VM bọc try/catch và nuốt lỗi (chat/đọc vẫn chạy, chỉ không persist lượt đó), nhất quán với cách `StartBackgroundIndexing` xử lý lỗi index. Store tự nó để lỗi nổi lên (test bắt được); VM là nơi nuốt.

## Luồng dữ liệu (tóm tắt)

```
Mở/đổi sách: set _documentId -> LoadChatHistory()
  -> ChatMessages.Clear()
  -> GetAll(docId): rỗng? chào : nạp bong bóng + SeedHistory(turns)

Gửi tin: thêm bong bóng User -> (RAG/context) -> stream AI -> bong bóng AI
  -> Append(User, question) + Append(AI, answer)

Xóa sách: Remove(item) -> DeleteForDocument(docId)
```

## Kiểm thử

**`SqliteChatHistoryStore`** (store thật, file tạm):
- Append nhiều lượt rồi `GetAll` trả đúng thứ tự thời gian tăng dần.
- Cô lập theo `documentId`: lịch sử sách A không lẫn sách B.
- `DeleteForDocument` xóa sạch đúng sách đó, không đụng sách khác.
- `GetAll` documentId chưa có gì → rỗng.

**`AiChatService.SeedHistory`** (qua fake `IChatClient` đã có trong test):
- Seed các lượt cũ rồi `AskStreamingAsync` → request gửi cho client chứa các lượt User/Assistant cũ (đúng thứ tự, nội dung sạch, không khối ngữ cảnh) đứng trước câu hỏi mới.
- Seed danh sách rỗng → chỉ còn System prompt.

**`MainViewModel`** (store thật file tạm hoặc fake in-memory, fake services, không mạng):
- Mở sách có lịch sử → `ChatMessages` nạp lại đúng số/đúng nội dung.
- Đổi sang sách khác → `ChatMessages` bị thay, không còn bong bóng sách cũ.
- Mở sách rỗng lịch sử → đúng 1 bong bóng chào.
- Sau `SendMessage` → store có đúng 2 dòng (User + AI) cho documentId đó.
- `RemoveLibraryItem` → store không còn lịch sử của sách đó.

## Phạm vi loại trừ (YAGNI)

- Không có nút "xóa chat / cuộc trò chuyện mới" riêng.
- Không có nhiều phiên/đặt tên hội thoại cho một sách (chỉ một luồng liên tục).
- Không tỉa/giới hạn độ dài lịch sử LLM trong phạm vi này (giới hạn `MaxContextChars` hiện có vẫn áp cho phần ngữ cảnh tài liệu; nếu lịch sử quá dài gây lỗi token, xử lý ở backlog sau).
