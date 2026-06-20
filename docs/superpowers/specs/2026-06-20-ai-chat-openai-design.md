# Thiết kế: Nối LLM thật cho AIService — Sub-project 1 (OpenAI Chat)

**Ngày:** 2026-06-20
**Trạng thái:** Đã duyệt design, chờ implementation plan
**Động cơ:** `AIService` hiện là placeholder (`Task.Delay` trả chuỗi giả). Mục tiêu Sub-project 1: chat AI **thật** qua OpenAI, có streaming, nhớ hội thoại, quản lý API key an toàn — dùng text tài liệu trích từ iText (Phase 1) làm ngữ cảnh.

## 0. Phân rã & phạm vi

Yêu cầu đầy đủ (OpenAI + Settings UI mã hóa + RAG embeddings + streaming + lịch sử) là nhiều hệ con. Đã tách:

- **Sub-project 1 (spec này):** OpenAI Chat end-to-end — `IChatClient` (Microsoft.Extensions.AI) + provider OpenAI, streaming, lịch sử hội thoại, quản lý key (Settings UI + DPAPI). Context tài liệu = **nhồi tạm theo trang hiện tại**.
- **Sub-project 2 (spec riêng sau):** SQLite index + RAG + Search (FTS5) — thay phần nhồi tạm bằng truy hồi top-k chunk theo embeddings; FTS5 phục vụ luôn feature Search trên toolbar.

Ranh giới `AskStreamingAsync(question, context)` **không đổi** giữa hai sub-project — chỉ nguồn của `context` thay đổi (trang hiện tại → top-k RAG).

## 1. Quyết định đã chốt

| Hạng mục | Quyết định |
|---|---|
| Provider | OpenAI, **qua lớp trừu tượng Microsoft.Extensions.AI** (`IChatClient`) để đổi provider sau không sửa logic |
| SDK | `Microsoft.Extensions.AI` + `Microsoft.Extensions.AI.OpenAI` + `OpenAI` |
| API key | Settings UI (MaterialDesign dialog) + lưu mã hóa **DPAPI** (`ProtectedData`, scope `CurrentUser`) tại `%APPDATA%\PdfReaderApp\settings.dat` |
| Hiển thị | **Streaming token** (hiện dần như ChatGPT) |
| Lịch sử | Nhớ **cả phiên** (multi-turn) |
| Context tạm | **Luôn quanh trang hiện tại** (trang đang xem ± 2 trang), bất kể tài liệu lớn nhỏ |

## 2. Kiến trúc & Components

```
┌─────────── MainViewModel (chat UI) ───────────┐
│  SendMessage → stream tokens vào ChatMessages  │
└───────┬─────────────────────────────┬─────────┘
        │ chat                         │ đọc/ghi key
┌───────▼──────────┐         ┌─────────▼──────────┐
│  AiChatService   │         │  ISettingsService  │
│  (app-level)     │         │  (DPAPI encrypt)   │
└───────┬──────────┘         └────────────────────┘
        │ dùng
┌───────▼─────────────────────────────┐
│  Microsoft.Extensions.AI.IChatClient │  ← interface chuẩn (lớp C)
│  ← OpenAI provider (.AsIChatClient)  │  ← SDK OpenAI (lớp A)
└──────────────────────────────────────┘
```

### Thành phần mới

- **`AiChatService`** (thay `AIService`): dựng danh sách message M.E.AI (system prompt + lịch sử + context tài liệu + câu hỏi mới), gọi `IChatClient.GetStreamingResponseAsync`, trả token ra `IAsyncEnumerable<string>`; gom token thành câu trả lời đầy đủ rồi lưu vào lịch sử. Chỉ phụ thuộc `IChatClient` (M.E.AI), không biết tới OpenAI SDK.
- **`IChatClient`** (Microsoft.Extensions.AI): interface chuẩn trung lập provider — **không tự định nghĩa**, dùng của Microsoft.
- **OpenAI provider:** `Microsoft.Extensions.AI.OpenAI` bọc SDK `OpenAI` thành `IChatClient` qua `.AsIChatClient()`. Dựng ở composition root, gắn API key.
- **`ISettingsService` + `WindowsSettingsService`:** đọc/ghi API key mã hóa DPAPI.

### Dependencies thêm
`Microsoft.Extensions.AI`, `Microsoft.Extensions.AI.OpenAI`, `OpenAI`.

### Interface dự kiến

```csharp
// AiChatService
IAsyncEnumerable<string> AskStreamingAsync(
    string question, string documentContext, CancellationToken ct = default);
void ResetConversation(); // xóa lịch sử, giữ system prompt

// ISettingsService
string? GetApiKey();
void SaveApiKey(string apiKey);
bool HasApiKey();
```

## 3. Luồng dữ liệu một lượt chat

**Trạng thái trong `AiChatService`:** một `List<ChatMessage>` (M.E.AI), khởi tạo với 1 message `System` (trợ lý PDF tiếng Việt, chỉ trả lời dựa trên tài liệu; nếu chưa có tài liệu thì nói rõ).

**Mỗi lượt:**
```
1. VM lọc TextBlock theo trang: CurrentPage ± 2 → join text → documentContext
2. VM gọi AiChatService.AskStreamingAsync(question, documentContext)
3. Service:
   a. Chèn/cập nhật một message chứa documentContext (cắt theo ngân sách token an toàn)
   b. Add ChatMessage(User, question)
   c. Gọi _chatClient.GetStreamingResponseAsync(lịch sử)
   d. yield từng update.Text (token)
   e. Gom token → câu trả lời đầy đủ → Add ChatMessage(Assistant, full)
4. VM:
   - Add 1 ChatMessage(Role="AI", Content="") rỗng vào ChatMessages
   - foreach token: nối vào Content qua Dispatcher (UI cập nhật dần)
```

**Lưu ý:**
- Context lọc theo trang nằm ở **VM** (có `CurrentPage` + `TextBlock.PageIndex` từ Phase 1, qua `PdfStructureAnalyzer.AnalyzeRich()`). `AiChatService` page-agnostic, chỉ nhận string.
- Streaming cập nhật `Content` message cuối **qua `Dispatcher`** (token đến từ thread khác).
- `ChatMessage` của VM (`Role`/`Content`/`Timestamp`) khác `ChatMessage` của M.E.AI — service dịch giữa hai loại; VM không tham chiếu M.E.AI.

## 4. Quản lý API key (Settings UI + DPAPI)

- **`WindowsSettingsService`:** lưu key đã mã hóa vào `%APPDATA%\PdfReaderApp\settings.dat` (đường dẫn qua `Environment.GetFolderPath(SpecialFolder.ApplicationData)`). Mã hóa bằng DPAPI `ProtectedData.Protect/Unprotect`, scope `CurrentUser` — key chỉ giải mã được bởi đúng user Windows; không lưu plaintext, không hardcode trong source.
- **Settings UI:** MaterialDesign dialog mở từ toolbar; `PasswordBox` nhập key, nút Lưu/Hủy.
- **Composition root (App):** đọc key qua `ISettingsService` → dựng OpenAI `IChatClient` → tiêm `AiChatService` → tiêm `MainViewModel`. Đổi key trong Settings → dựng lại `IChatClient`, có hiệu lực ngay (không restart).
- Lý do `%APPDATA%`: thư mục quy ước cấu hình per-user, luôn ghi được (khác `Program Files` chỉ-đọc), tách theo user. Không lưu cạnh PDF (PDF có thể ở USB/OneDrive, sẽ rò key).

## 5. Xử lý lỗi

Mọi lỗi LLM/IO được bắt trong `AiChatService` hoặc VM → chuyển thành **message tiếng Việt thân thiện trong khung chat**; không để exception nổi lên `DispatcherUnhandledException`.

| Tình huống | Xử lý |
|---|---|
| Chưa cấu hình key | Chặn trước khi gọi API; message + mở Settings; không ném exception |
| Key sai / 401 | "API key không hợp lệ, vui lòng kiểm tra lại trong Cài đặt" |
| Hết quota / 429 | "Đã vượt giới hạn yêu cầu, thử lại sau" (không auto-retry — YAGNI) |
| Mất mạng / timeout | "Không kết nối được dịch vụ AI, kiểm tra mạng" |
| Lỗi giữa chừng streaming | Giữ token đã nhận + nối "...[lỗi: phản hồi bị gián đoạn]"; lưu lượt dở vào lịch sử |
| Tài liệu chưa mở | Vẫn chat (general); system prompt nhắc "chưa có tài liệu" |
| settings.dat hỏng / giải mã fail | Coi như chưa có key (degrade), không crash; user nhập lại |

## 6. Testing (xUnit, project `PdfReaderApp.Tests`)

Mock `IChatClient` bằng fake class tự viết (M.E.AI là interface; không cần thư viện mock).

| Nhóm test | Nội dung |
|---|---|
| Dựng message | Fake ghi lại messages; assert đúng 1 system message, lịch sử đúng thứ tự, context được chèn, câu hỏi mới ở cuối |
| Streaming | Fake trả chuỗi `ChatResponseUpdate`; assert yield đúng token theo thứ tự |
| Lịch sử nhiều lượt | Gọi 2 lượt; assert lượt 2 chứa Q+A lượt 1 trong messages gửi đi |
| Lỗi | Fake ném exception giữa stream; assert giữ token đã nhận + không ném ra ngoài |
| Settings round-trip | Save → Get trả đúng key; file là bytes mã hóa (không chứa plaintext); dùng thư mục temp, dọn sau |
| Settings chưa có key | `HasApiKey()` false khi file chưa tồn tại / hỏng |

**Không test:** gọi OpenAI thật, Settings UI dialog (kiểm thủ công), composition root. Test DPAPI round-trip chạy trên Windows (môi trường dev).

## 7. Quyết định đã chốt (tóm tắt)
- Provider OpenAI qua abstraction Microsoft.Extensions.AI (đổi provider sau dễ).
- Key: DPAPI `CurrentUser` + `%APPDATA%\PdfReaderApp\settings.dat` + Settings UI.
- Streaming token; lịch sử cả phiên; context tạm = trang hiện tại ± 2.
- Ranh giới `AskStreamingAsync(question, context)` ổn định để Sub-project 2 (RAG) cắm vào không phá UI.

## 8. Ngoài phạm vi (để Sub-project 2)
- SQLite index (FTS5 + embeddings BLOB), chunking, vector retrieval thay nhồi tạm.
- Feature Search trên toolbar (dùng FTS5).
- Settings UI nâng cao (chọn model, nhiệt độ), đa provider runtime.
