# Lưu lịch sử chat theo từng quyển sách — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mỗi quyển sách có luồng chat riêng được lưu bền vững trong SQLite; mở lại sách thì hiện lại tin nhắn cũ và AI nhớ tiếp mạch hội thoại.

**Architecture:** Thêm store SQLite mới `chats.db` (`IChatHistoryStore` + `SqliteChatHistoryStore`) khóa theo `documentId`, đúng pattern `SqliteLibraryStore`. `AiChatService` thêm `SeedHistory(...)` để dựng lại lịch sử LLM từ câu hỏi/đáp sạch. `MainViewModel` nạp lịch sử khi mở/đổi sách, ghi 2 dòng mỗi lượt `SendMessage`, và xóa lịch sử khi xóa sách.

**Tech Stack:** WPF, .NET net10.0-windows, CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, Microsoft.Extensions.AI, xUnit.

## Global Constraints

- Comment/chuỗi UI tiếng Việt phải GIỮ DẤU (không strip về ASCII).
- Không dùng dấu gạch ngang dài (em dash) trong code/commit/comment.
- Store SQLite: connection per-operation, chuỗi kết nối có `Pooling=False` (nhất quán `SqliteLibraryStore`).
- Nội dung lưu là câu hỏi/đáp SẠCH (`Role` + `Content`), KHÔNG kèm khối "Ngữ cảnh tài liệu".
- Lỗi store trong `MainViewModel` phải bị nuốt (try/catch) để không làm hỏng đọc/chat; store tự để lỗi nổi lên.
- Khóa lịch sử = `_documentId` (SHA256 nội dung, từ `DocumentId.FromFile`).
- Không thêm nút "xóa chat" riêng, không nhiều phiên hội thoại (YAGNI).

---

### Task 1: Model + store SQLite cho lịch sử chat

**Files:**
- Create: `src/PdfReaderApp/Models/ChatHistoryEntry.cs`
- Create: `src/PdfReaderApp/Services/IChatHistoryStore.cs`
- Create: `src/PdfReaderApp/Services/SqliteChatHistoryStore.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SqliteChatHistoryStoreTests.cs`

**Interfaces:**
- Consumes: nothing (lớp nền).
- Produces:
  - `record ChatHistoryEntry(string DocumentId, string Role, string Content, long CreatedAtUnix)` trong namespace `PdfReaderApp.Models`.
  - `interface IChatHistoryStore { void EnsureSchema(); void Append(string documentId, string role, string content, long createdAtUnix); IReadOnlyList<ChatHistoryEntry> GetAll(string documentId); void DeleteForDocument(string documentId); }` trong `PdfReaderApp.Services`.
  - `sealed class SqliteChatHistoryStore : IChatHistoryStore` với ctor `SqliteChatHistoryStore(string dbPath)`.

- [ ] **Step 1: Viết test thất bại**

Tạo `tests/PdfReaderApp.Tests/Services/SqliteChatHistoryStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteChatHistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteChatHistoryStore _store;

    public SqliteChatHistoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _store = new SqliteChatHistoryStore(Path.Combine(_dir, "chats.db"));
        _store.EnsureSchema();
    }

    [Fact]
    public void Append_Then_GetAll_ReturnsInChronologicalOrder()
    {
        _store.Append("doc1", "User", "Câu hỏi 1", 100);
        _store.Append("doc1", "AI", "Trả lời 1", 101);
        _store.Append("doc1", "User", "Câu hỏi 2", 102);

        var all = _store.GetAll("doc1");

        Assert.Equal(3, all.Count);
        Assert.Equal("User", all[0].Role);
        Assert.Equal("Câu hỏi 1", all[0].Content);
        Assert.Equal("AI", all[1].Role);
        Assert.Equal("Câu hỏi 2", all[2].Content);
    }

    [Fact]
    public void GetAll_IsolatesByDocumentId()
    {
        _store.Append("docA", "User", "thuộc A", 1);
        _store.Append("docB", "User", "thuộc B", 2);

        var a = _store.GetAll("docA");

        Assert.Single(a);
        Assert.Equal("thuộc A", a[0].Content);
    }

    [Fact]
    public void DeleteForDocument_RemovesOnlyThatDocument()
    {
        _store.Append("docA", "User", "a1", 1);
        _store.Append("docB", "User", "b1", 2);

        _store.DeleteForDocument("docA");

        Assert.Empty(_store.GetAll("docA"));
        Assert.Single(_store.GetAll("docB"));
    }

    [Fact]
    public void GetAll_UnknownDocument_ReturnsEmpty()
    {
        Assert.Empty(_store.GetAll("khong-ton-tai"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~SqliteChatHistoryStoreTests"`
Expected: FAIL biên dịch (`SqliteChatHistoryStore` chưa tồn tại).

- [ ] **Step 3: Tạo model**

`src/PdfReaderApp/Models/ChatHistoryEntry.cs`:

```csharp
namespace PdfReaderApp.Models;

/// <summary>Một tin nhắn chat đã lưu, gắn với một documentId.</summary>
public sealed record ChatHistoryEntry(string DocumentId, string Role, string Content, long CreatedAtUnix);
```

- [ ] **Step 4: Tạo interface**

`src/PdfReaderApp/Services/IChatHistoryStore.cs`:

```csharp
using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu lịch sử chat theo từng documentId trong chats.db.</summary>
public interface IChatHistoryStore
{
    void EnsureSchema();
    void Append(string documentId, string role, string content, long createdAtUnix);
    IReadOnlyList<ChatHistoryEntry> GetAll(string documentId);
    void DeleteForDocument(string documentId);
}
```

- [ ] **Step 5: Tạo store SQLite**

`src/PdfReaderApp/Services/SqliteChatHistoryStore.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu lịch sử chat trong chats.db, tách khỏi library.db và index.db.</summary>
public sealed class SqliteChatHistoryStore : IChatHistoryStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteChatHistoryStore(string dbPath)
    {
        _connectionString = $"Data Source={dbPath};Pooling=False";
    }

    private SqliteConnection OpenConn()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        return conn;
    }

    public void EnsureSchema()
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS chat_message (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  document_id TEXT NOT NULL,
  role TEXT NOT NULL,
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_chat_message_doc ON chat_message(document_id, id);";
            cmd.ExecuteNonQuery();
        }
    }

    public void Append(string documentId, string role, string content, long createdAtUnix)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO chat_message (document_id, role, content, created_at)
VALUES ($id, $role, $content, $ts);";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.Parameters.AddWithValue("$role", role);
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$ts", createdAtUnix);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId)
    {
        lock (_lock)
        {
            var list = new List<ChatHistoryEntry>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, role, content, created_at FROM chat_message WHERE document_id=$id ORDER BY id ASC";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new ChatHistoryEntry(r.GetString(0), r.GetString(1), r.GetString(2), r.GetInt64(3)));
            return list;
        }
    }

    public void DeleteForDocument(string documentId)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM chat_message WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }
}
```

- [ ] **Step 6: Chạy test, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~SqliteChatHistoryStoreTests"`
Expected: PASS 4/4.

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/Models/ChatHistoryEntry.cs src/PdfReaderApp/Services/IChatHistoryStore.cs src/PdfReaderApp/Services/SqliteChatHistoryStore.cs tests/PdfReaderApp.Tests/Services/SqliteChatHistoryStoreTests.cs
git commit -m "feat: add SqliteChatHistoryStore for per-book chat history"
```

---

### Task 2: `AiChatService.SeedHistory` để AI nhớ tiếp mạch

**Files:**
- Modify: `src/PdfReaderApp/Services/AiChatService.cs`
- Test: `tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs`

**Interfaces:**
- Consumes: kiểu `ChatMessage`/`ChatRole` của Microsoft.Extensions.AI (đã dùng trong file).
- Produces: `void SeedHistory(IEnumerable<(string role, string content)> turns)` trên `AiChatService`. Map `role == "AI"` → `ChatRole.Assistant`, còn lại → `ChatRole.User`. Sau khi seed, `_history` = System prompt + các lượt theo thứ tự, nội dung thô (không bọc ngữ cảnh).

- [ ] **Step 1: Viết test thất bại**

Thêm vào cuối lớp `AiChatServiceTests` trong `tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs` (trước dấu `}` đóng lớp):

```csharp
    [Fact]
    public async Task SeedHistory_IncludesPriorTurnsBeforeNewQuestion()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        svc.SeedHistory(new[]
        {
            ("User", "Hỏi cũ"),
            ("AI", "Đáp cũ"),
        });
        await Collect(svc.AskStreamingAsync("Hỏi mới", "ctx"));

        var msgs = client.LastMessages;
        // System đứng đầu, rồi 2 lượt cũ, rồi câu hỏi mới ở cuối.
        Assert.Equal(ChatRole.System, msgs.First().Role);
        var texts = msgs.Select(m => m.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("Hỏi cũ"));
        Assert.Contains(texts, t => t.Contains("Đáp cũ"));
        Assert.Equal(ChatRole.Assistant, msgs[2].Role); // lượt "AI" cũ map sang Assistant
        Assert.Contains("Hỏi mới", msgs.Last().Text);
        Assert.Equal(ChatRole.User, msgs.Last().Role);
        // Lịch sử cũ là nội dung sạch, không nhúng khối ngữ cảnh tài liệu.
        Assert.DoesNotContain("Ngữ cảnh tài liệu", msgs[1].Text);
    }

    [Fact]
    public async Task SeedHistory_Empty_LeavesOnlySystemPrompt()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        svc.SeedHistory(System.Array.Empty<(string, string)>());
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var texts = client.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Hỏi 1"));
        Assert.Contains(texts, t => t.Contains("Hỏi 2"));
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~AiChatServiceTests.SeedHistory"`
Expected: FAIL biên dịch (`SeedHistory` chưa tồn tại).

- [ ] **Step 3: Thêm `SeedHistory`**

Trong `src/PdfReaderApp/Services/AiChatService.cs`, ngay sau method `ResetConversation()` (kết thúc ở dòng `}` của nó), thêm:

```csharp
    /// <summary>Dựng lại lịch sử hội thoại LLM từ các lượt đã lưu (nội dung sạch, không kèm
    /// khối ngữ cảnh tài liệu). Dùng khi mở lại một sách để AI nhớ tiếp mạch.</summary>
    public void SeedHistory(IEnumerable<(string role, string content)> turns)
    {
        _history.Clear();
        _history.Add(new ChatMessage(ChatRole.System, SystemPrompt));
        foreach (var (role, content) in turns)
        {
            var chatRole = role == "AI" ? ChatRole.Assistant : ChatRole.User;
            _history.Add(new ChatMessage(chatRole, content));
        }
    }
```

- [ ] **Step 4: Chạy test, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~AiChatServiceTests"`
Expected: PASS (toàn bộ test cũ + 2 test mới).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/AiChatService.cs tests/PdfReaderApp.Tests/Services/AiChatServiceTests.cs
git commit -m "feat: AiChatService.SeedHistory restores conversation memory"
```

---

### Task 3: Nối `MainViewModel` — nạp/lưu/xóa lịch sử chat

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `IChatHistoryStore`, `SqliteChatHistoryStore`, `ChatHistoryEntry` (Task 1); `AiChatService.SeedHistory` (Task 2).
- Produces: trường `_chatHistory` và hành vi nạp/lưu/xóa trong VM; tham số ctor tùy chọn `IChatHistoryStore? chatHistory = null` (mặc định dựng `SqliteChatHistoryStore` thật) để test inject được fake.

**Bối cảnh code hiện tại (đã đọc):**
- Ctor inject hiện là `MainViewModel(IPdfDocumentService, ISettingsService, IChatClientFactory, IDocumentIndex, IEmbeddingGeneratorFactory)`. Cuối ctor thêm 1 `ChatMessage` chào.
- `LoadActiveDocument(string path)` set `_documentId`, gọi `_chatService.ResetConversation()`. Nhánh catch set `_documentId = null`.
- `SendMessage()` thêm bong bóng User; nếu `!_chatService.IsConfigured` thêm bong bóng "Chưa cấu hình..." rồi `return`; ngược lại stream và thêm bong bóng AI.
- `RemoveLibraryItem(LibraryItem? item)` gọi `_library.Remove(item)` + `Library.Remove(item)`.
- Test hiện chỉ dùng `new MainViewModel()` (ctor không tham số) → thêm tham số tùy chọn KHÔNG phá test cũ.

- [ ] **Step 1: Viết test thất bại (RemoveLibraryItem xóa lịch sử)**

Mở `tests/PdfReaderApp.Tests/MainViewModelTests.cs`. Thêm fake store + test mới. Thêm `using`/namespace cần thiết ở đầu file nếu thiếu (`using System.Collections.Generic;`, `using System.Linq;`, `using PdfReaderApp.Models;`, `using PdfReaderApp.Services;`). Thêm vào trong lớp test:

```csharp
    private sealed class FakeChatHistoryStore : PdfReaderApp.Services.IChatHistoryStore
    {
        public readonly List<string> Deleted = new();
        public readonly List<(string doc, string role, string content)> Appended = new();
        private readonly List<PdfReaderApp.Models.ChatHistoryEntry> _entries = new();

        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix)
        {
            Appended.Add((documentId, role, content));
            _entries.Add(new PdfReaderApp.Models.ChatHistoryEntry(documentId, role, content, createdAtUnix));
        }
        public System.Collections.Generic.IReadOnlyList<PdfReaderApp.Models.ChatHistoryEntry> GetAll(string documentId)
            => _entries.Where(e => e.DocumentId == documentId).ToList();
        public void DeleteForDocument(string documentId) => Deleted.Add(documentId);
    }

    private static MainViewModel VmWithChatStore(FakeChatHistoryStore store)
        => new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.OpenAiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db"),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.OpenAiEmbeddingGeneratorFactory(),
            store);

    [Fact]
    public void RemoveLibraryItem_DeletesChatHistoryForThatDocument()
    {
        var store = new FakeChatHistoryStore();
        var vm = VmWithChatStore(store);
        var item = new PdfReaderApp.Models.LibraryItem("docX", "x.pdf", "/lib/x.pdf", null, 3, 1, 1);
        vm.Library.Add(item);

        vm.RemoveLibraryItemCommand.Execute(item);

        Assert.Contains("docX", store.Deleted);
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests.RemoveLibraryItem_DeletesChatHistory"`
Expected: FAIL biên dịch (ctor 6 tham số chưa tồn tại).

- [ ] **Step 3: Thêm tham số ctor + trường `_chatHistory`**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`:

3a. Thêm trường cạnh `_library` (sau dòng `private readonly LibraryService _library;`):

```csharp
    private readonly IChatHistoryStore _chatHistory;
```

3b. Sửa chữ ký ctor inject, thêm tham số tùy chọn cuối:

```csharp
    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory,
        IDocumentIndex documentIndex,
        IEmbeddingGeneratorFactory embeddingFactory,
        IChatHistoryStore? chatHistory = null)
    {
```

3c. Trong thân ctor, ngay sau khối dựng `_library` (`ReloadLibrary();`), trước khối thêm `ChatMessage` chào, thêm:

```csharp
        _chatHistory = chatHistory ?? new SqliteChatHistoryStore(System.IO.Path.Combine(AppDir(), "chats.db"));
        _chatHistory.EnsureSchema();
```

3d. Thay khối seed lời chào ở cuối ctor:

```csharp
        ChatMessages.Add(new ChatMessage
        {
            Role = "AI",
            Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
        });
```

bằng:

```csharp
        LoadChatHistory();
```

- [ ] **Step 4: Thêm `LoadChatHistory()` và đổi `LoadActiveDocument`**

4a. Thêm method private (đặt ngay sau `LoadActiveDocument`):

```csharp
    // Nạp lại khung chat theo sách đang mở: hiện bong bóng cũ và dựng lại bộ nhớ LLM.
    // Sách chưa có lịch sử (hoặc chưa mở sách nào) -> hiện 1 bong bóng chào, reset LLM.
    private void LoadChatHistory()
    {
        ChatMessages.Clear();

        if (_documentId is null)
        {
            ShowGreeting();
            _chatService.ResetConversation();
            return;
        }

        System.Collections.Generic.IReadOnlyList<ChatHistoryEntry> entries;
        try { entries = _chatHistory.GetAll(_documentId); }
        catch { entries = System.Array.Empty<ChatHistoryEntry>(); }

        if (entries.Count == 0)
        {
            ShowGreeting();
            _chatService.ResetConversation();
            return;
        }

        foreach (var e in entries)
            ChatMessages.Add(new ChatMessage { Role = e.Role, Content = e.Content });
        _chatService.SeedHistory(System.Linq.Enumerable.Select(entries, e => (e.Role, e.Content)));
    }

    private void ShowGreeting() => ChatMessages.Add(new ChatMessage
    {
        Role = "AI",
        Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
    });
```

4b. Trong `LoadActiveDocument`, ở khối `try`, thay dòng:

```csharp
            _chatService.ResetConversation();
```

bằng:

```csharp
            LoadChatHistory();
```

4c. Trong khối `catch` của `LoadActiveDocument`, sau `_documentId = null;` thêm:

```csharp
            LoadChatHistory();
```

(để khung chat trở về trạng thái chào khi mở file lỗi).

- [ ] **Step 5: Ghi lịch sử trong `SendMessage`**

5a. Trong `SendMessage()`, ngay đầu khối `try` (sau `string question = ChatInput;` và `ChatInput = string.Empty;`), giữ nguyên. Thêm helper cục bộ ngay sau dòng `ChatMessages.Add(new ChatMessage { Role = "User", Content = question });`:

```csharp
            void PersistTurn(string answer)
            {
                if (_documentId is null) return;
                try
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                    _chatHistory.Append(_documentId, "User", question, now);
                    _chatHistory.Append(_documentId, "AI", answer, now);
                }
                catch { /* không chặn chat khi lưu lỗi */ }
            }
```

5b. Trong nhánh chưa cấu hình API key, ngay TRƯỚC `return;`, sau khi thêm bong bóng "Chưa cấu hình...", thêm:

```csharp
                PersistTurn("Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key.");
```

Cụ thể nhánh đó trở thành:

```csharp
            if (!_chatService.IsConfigured)
            {
                ChatMessages.Add(new ChatMessage
                {
                    Role = "AI",
                    Content = "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key."
                });
                PersistTurn("Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key.");
                return;
            }
```

5c. Ở CUỐI khối `try` (sau khối stream `try/catch` gán `aiMessage.Content`, ngay trước khi khối `try` lớn kết thúc và chuyển sang `finally`), thêm:

```csharp
            PersistTurn(aiMessage.Content);
```

- [ ] **Step 6: Xóa lịch sử trong `RemoveLibraryItem`**

Trong `RemoveLibraryItem`, sau `_library.Remove(item);` (trước hoặc sau `Library.Remove(item);`) thêm:

```csharp
        try { _chatHistory.DeleteForDocument(item.DocumentId); } catch { }
```

Method trở thành:

```csharp
    [RelayCommand]
    private void RemoveLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        _library.Remove(item);
        try { _chatHistory.DeleteForDocument(item.DocumentId); } catch { }
        Library.Remove(item);
    }
```

- [ ] **Step 7: Chạy test + build**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công.

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (test cũ + `RemoveLibraryItem_DeletesChatHistoryForThatDocument`).

- [ ] **Step 8: Chạy toàn bộ test**

Run: `dotnet test`
Expected: PASS toàn bộ (số cũ + 4 store + 2 SeedHistory + 1 RemoveLibraryItem).

- [ ] **Step 9: Commit**

```bash
git add src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: wire per-book chat history into MainViewModel"
```

- [ ] **Step 10: Manual GUI verify (sau khi merge code, controller nhờ người dùng chạy app)**

Các hành vi cần một tài liệu thật nên kiểm bằng tay (đúng tiền lệ tính năng thư viện):
1. Mở sách A, chat vài lượt. Đóng/mở sách B. Quay lại A → tin nhắn cũ của A hiện lại; bong bóng của B không lẫn.
2. Hỏi tiếp ở A một câu phụ thuộc lượt trước (ví dụ "tóm tắt lại ý vừa rồi") → AI trả lời đúng mạch (nhớ).
3. Mở sách chưa từng chat → hiện đúng 1 bong bóng chào.
4. Xóa sách A khỏi thư viện rồi import lại → khung chat của A trống (chỉ còn chào).

---

## Self-Review

**1. Spec coverage:**
- Store SQLite chats.db + khóa documentId → Task 1. ✅
- AI nhớ tiếp mạch (SeedHistory, nội dung sạch) → Task 2. ✅
- Nạp lại bong bóng khi mở/đổi sách + sửa lỗi bong bóng cũ → Task 3 Step 4 (LoadChatHistory clear + nạp). ✅
- Lời chào chỉ khi rỗng, không lưu DB → Task 3 Step 4 (ShowGreeting chỉ khi entries rỗng / docId null). ✅
- Ghi 2 dòng mỗi lượt, câu hỏi sạch → Task 3 Step 5 (PersistTurn). ✅
- Xóa sách → xóa lịch sử → Task 3 Step 6. ✅
- Lỗi store bị nuốt ở VM → Task 3 (try/catch quanh GetAll/Append/Delete). ✅
- YAGNI (không nút xóa chat, không nhiều phiên) → không có task nào thêm chúng. ✅

**2. Placeholder scan:** Không có TBD/TODO; mọi step có code/lệnh cụ thể.

**3. Type consistency:** `IChatHistoryStore` (EnsureSchema/Append/GetAll/DeleteForDocument), `ChatHistoryEntry(DocumentId, Role, Content, CreatedAtUnix)`, `SeedHistory(IEnumerable<(string role, string content)>)`, `SqliteChatHistoryStore(string dbPath)` — dùng nhất quán giữa Task 1/2/3 và test.
