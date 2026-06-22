# Notes Layer 1 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mỗi quyển sách có tập ghi chú dạng thẻ rời (tạo/sửa/xóa, tự neo trang, lọc nhanh), lưu SQLite, hiện trong tab Notes của sidebar phải.

**Architecture:** Store SQLite `notes.db` workspace-ready (owner_key + anchor nullable + GUID + user_version). `NotesViewModel` riêng (test trọn vẹn) giữ danh sách lọc/sắp trong bộ nhớ và cập nhật tại chỗ. `MainWindow.xaml` đổi nội dung sidebar phải thành `TabControl` MaterialDesign (Chat | Notes).

**Tech Stack:** WPF .NET 10, CommunityToolkit.Mvvm, MaterialDesignThemes 5.1.0, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Comment/chuỗi UI tiếng Việt GIỮ DẤU (không strip ASCII).
- Không dùng dấu gạch ngang dài (em dash).
- Store SQLite: connection per-operation, chuỗi kết nối có `Pooling=False`, có `lock` (như `SqliteChatHistoryStore`).
- Note model: `Note(string Id, string OwnerKey, string? DocumentId, int? PageIndex, string Content, long CreatedAtUnixMs, long UpdatedAtUnixMs)`. v1: OwnerKey = DocumentId = documentId của sách; PageIndex = trang lúc tạo.
- Định danh = GUID (`Guid.NewGuid().ToString("N")`); timestamp = `DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()`.
- `PRAGMA user_version = 1` trong EnsureSchema.
- Cập nhật `ObservableCollection` TẠI CHỖ khi add/edit/delete (không reload toàn bộ); chỉ reload khi đổi sách hoặc đổi bộ lọc.
- Sắp xếp ở VM: PageIndex tăng dần (null cuối), rồi CreatedAtUnixMs giảm dần. Lọc: OrdinalIgnoreCase contains trên Content.
- Sidebar phải: `TabControl` style `MaterialDesignTabControl`, 2 TabItem (Chat | Notes); giữ nguyên Visibility theo `ShowLibrary` + width binding hiện có.
- Lệnh trong DataTemplate bind qua `RelativeSource AncestorType=ItemsControl` → `DataContext.Notes.{...}Command`.
- Lời gọi store trong VM bọc try/catch nuốt lỗi (đọc/chat không hỏng nếu store lỗi); store để lỗi nổi lên.

---

### Task 1: Note model + INoteStore + SqliteNoteStore

**Files:**
- Create: `src/PdfReaderApp/Models/Note.cs`
- Create: `src/PdfReaderApp/Services/INoteStore.cs`
- Create: `src/PdfReaderApp/Services/SqliteNoteStore.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs`

**Interfaces:**
- Produces:
  - `record Note(string Id, string OwnerKey, string? DocumentId, int? PageIndex, string Content, long CreatedAtUnixMs, long UpdatedAtUnixMs)` (namespace `PdfReaderApp.Models`).
  - `interface INoteStore { void EnsureSchema(); void Add(Note note); int Update(string id, string content, long nowUnixMs); int Delete(string id); IReadOnlyList<Note> GetForOwner(string ownerKey); }` (namespace `PdfReaderApp.Services`).
  - `sealed class SqliteNoteStore : INoteStore` với ctor `SqliteNoteStore(string dbPath)`.

- [ ] **Step 1: Viết test thất bại**

Tạo `tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteNoteStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteNoteStore _store;

    public SqliteNoteStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _store = new SqliteNoteStore(Path.Combine(_dir, "notes.db"));
        _store.EnsureSchema();
    }

    private static Note N(string id, string owner, int? page, string content, long t) =>
        new(id, owner, owner, page, content, t, t);

    [Fact]
    public void Add_Then_GetForOwner_ReturnsNote()
    {
        _store.Add(N("a", "doc1", 3, "Ghi chú một", 100));
        var all = _store.GetForOwner("doc1");
        Assert.Single(all);
        Assert.Equal("Ghi chú một", all[0].Content);
        Assert.Equal(3, all[0].PageIndex);
        Assert.Equal("doc1", all[0].DocumentId);
    }

    [Fact]
    public void GetForOwner_IsolatesByOwnerKey()
    {
        _store.Add(N("a", "docA", 1, "thuộc A", 1));
        _store.Add(N("b", "docB", 1, "thuộc B", 2));
        Assert.Single(_store.GetForOwner("docA"));
        Assert.Equal("thuộc A", _store.GetForOwner("docA")[0].Content);
    }

    [Fact]
    public void Update_ChangesContentAndTimestamp_ReturnsRows()
    {
        _store.Add(N("a", "docA", 1, "cũ", 100));
        int rows = _store.Update("a", "mới", 200);
        Assert.Equal(1, rows);
        var got = _store.GetForOwner("docA").Single();
        Assert.Equal("mới", got.Content);
        Assert.Equal(200, got.UpdatedAtUnixMs);
        Assert.Equal(100, got.CreatedAtUnixMs);
    }

    [Fact]
    public void Update_UnknownId_ReturnsZero()
    {
        Assert.Equal(0, _store.Update("khong-co", "x", 1));
    }

    [Fact]
    public void Delete_RemovesNote_ReturnsRows()
    {
        _store.Add(N("a", "docA", 1, "x", 1));
        Assert.Equal(1, _store.Delete("a"));
        Assert.Empty(_store.GetForOwner("docA"));
    }

    [Fact]
    public void Delete_UnknownId_ReturnsZero()
    {
        Assert.Equal(0, _store.Delete("khong-co"));
    }

    [Fact]
    public void NullPageAndDocument_RoundTrip()
    {
        _store.Add(new Note("a", "docA", null, null, "tự do", 1, 1));
        var got = _store.GetForOwner("docA").Single();
        Assert.Null(got.PageIndex);
        Assert.Null(got.DocumentId);
    }

    [Fact]
    public void EnsureSchema_IsIdempotent()
    {
        _store.EnsureSchema();
        _store.EnsureSchema();
        Assert.Empty(_store.GetForOwner("docA"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~SqliteNoteStoreTests"`
Expected: FAIL biên dịch (`Note`/`SqliteNoteStore` chưa tồn tại).

- [ ] **Step 3: Tạo model**

`src/PdfReaderApp/Models/Note.cs`:

```csharp
namespace PdfReaderApp.Models;

/// <summary>Một ghi chú. v1: OwnerKey = DocumentId = documentId của sách; PageIndex = trang lúc tạo.
/// OwnerKey là phạm vi (sau này = workspaceId), DocumentId là anchor (doc mà note trỏ tới).</summary>
public sealed record Note(
    string Id,
    string OwnerKey,
    string? DocumentId,
    int? PageIndex,
    string Content,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs);
```

- [ ] **Step 4: Tạo interface**

`src/PdfReaderApp/Services/INoteStore.cs`:

```csharp
using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu ghi chú theo owner_key (v1 = documentId) trong notes.db.</summary>
public interface INoteStore
{
    void EnsureSchema();
    void Add(Note note);
    int Update(string id, string content, long nowUnixMs);
    int Delete(string id);
    IReadOnlyList<Note> GetForOwner(string ownerKey);
}
```

- [ ] **Step 5: Tạo store SQLite**

`src/PdfReaderApp/Services/SqliteNoteStore.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu ghi chú trong notes.db, tách khỏi library.db/chats.db/index.db.
/// Schema có PRAGMA user_version để migrate ở các layer sau (thêm cột tag/màu/region).</summary>
public sealed class SqliteNoteStore : INoteStore
{
    private const long SchemaVersion = 1;
    private readonly string _connectionString;
    private readonly object _lock = new();

    public SqliteNoteStore(string dbPath)
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
CREATE TABLE IF NOT EXISTS note (
  id TEXT PRIMARY KEY,
  owner_key TEXT NOT NULL,
  document_id TEXT,
  page_index INTEGER,
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_note_owner ON note(owner_key);";
            cmd.ExecuteNonQuery();

            // Khung migration theo phiên bản schema (các layer sau dùng ALTER TABLE ADD COLUMN).
            using var ver = conn.CreateCommand();
            ver.CommandText = "PRAGMA user_version;";
            long current = (long)(ver.ExecuteScalar() ?? 0L);
            if (current < SchemaVersion)
            {
                using var set = conn.CreateCommand();
                set.CommandText = $"PRAGMA user_version = {SchemaVersion};";
                set.ExecuteNonQuery();
            }
        }
    }

    public void Add(Note note)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO note (id, owner_key, document_id, page_index, content, created_at, updated_at)
VALUES ($id, $owner, $doc, $page, $content, $created, $updated);";
            cmd.Parameters.AddWithValue("$id", note.Id);
            cmd.Parameters.AddWithValue("$owner", note.OwnerKey);
            cmd.Parameters.AddWithValue("$doc", (object?)note.DocumentId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$page", (object?)note.PageIndex ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$content", note.Content);
            cmd.Parameters.AddWithValue("$created", note.CreatedAtUnixMs);
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAtUnixMs);
            cmd.ExecuteNonQuery();
        }
    }

    public int Update(string id, string content, long nowUnixMs)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "UPDATE note SET content=$content, updated_at=$updated WHERE id=$id";
            cmd.Parameters.AddWithValue("$content", content);
            cmd.Parameters.AddWithValue("$updated", nowUnixMs);
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery();
        }
    }

    public int Delete(string id)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM note WHERE id=$id";
            cmd.Parameters.AddWithValue("$id", id);
            return cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<Note> GetForOwner(string ownerKey)
    {
        lock (_lock)
        {
            var list = new List<Note>();
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT id, owner_key, document_id, page_index, content, created_at, updated_at FROM note WHERE owner_key=$owner";
            cmd.Parameters.AddWithValue("$owner", ownerKey);
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                list.Add(new Note(
                    r.GetString(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    r.GetString(4),
                    r.GetInt64(5),
                    r.GetInt64(6)));
            }
            return list;
        }
    }
}
```

- [ ] **Step 6: Chạy test, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~SqliteNoteStoreTests"`
Expected: PASS 8/8.

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/Models/Note.cs src/PdfReaderApp/Services/INoteStore.cs src/PdfReaderApp/Services/SqliteNoteStore.cs tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs
git commit -m "feat: add SqliteNoteStore for per-book notes (workspace-ready schema)"
```

---

### Task 2: NotesViewModel + nối MainViewModel

**Files:**
- Create: `src/PdfReaderApp/ViewModels/NotesViewModel.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`

**Interfaces:**
- Consumes: `INoteStore`, `Note` (Task 1).
- Produces:
  - `sealed partial class NotesViewModel : ObservableObject` với ctor `NotesViewModel(INoteStore store, Func<int?> currentPageIndex, Action<int> jumpToPageIndex)`.
  - Thuộc tính/lệnh: `ObservableCollection<Note> Items`; `[ObservableProperty] string Draft`; `[ObservableProperty] bool IsEditing`; `[ObservableProperty] string FilterText`; `[ObservableProperty] bool CanAddNote`; `[ObservableProperty] string StatusMessage`; `void LoadFor(string? ownerKey)`; lệnh `Save`, `BeginEdit(Note)`, `CancelEdit`, `Delete(Note)`, `Open(Note)`; `static bool MatchesFilter(Note, string?)`.
  - `MainViewModel.Notes` (kiểu `NotesViewModel`); ctor MainViewModel thêm tham số tùy chọn cuối `INoteStore? noteStore = null`.

- [ ] **Step 1: Viết test thất bại**

Tạo `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public class NotesViewModelTests
{
    private sealed class FakeNoteStore : INoteStore
    {
        public readonly List<Note> Rows = new();
        public void EnsureSchema() { }
        public void Add(Note note) => Rows.Add(note);
        public int Update(string id, string content, long now)
        {
            int i = Rows.FindIndex(n => n.Id == id);
            if (i < 0) return 0;
            Rows[i] = Rows[i] with { Content = content, UpdatedAtUnixMs = now };
            return 1;
        }
        public int Delete(string id) => Rows.RemoveAll(n => n.Id == id);
        public IReadOnlyList<Note> GetForOwner(string ownerKey)
            => Rows.Where(n => n.OwnerKey == ownerKey).ToList();
    }

    private static NotesViewModel Make(FakeNoteStore store, int? page, Action<int>? onJump = null)
        => new NotesViewModel(store, () => page, idx => onJump?.Invoke(idx));

    [Fact]
    public void Save_AddsNote_WithOwnerAndCurrentPage()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 5);
        vm.LoadFor("doc1");
        vm.Draft = "Ghi chú mới";

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("doc1", store.Rows[0].OwnerKey);
        Assert.Equal(5, store.Rows[0].PageIndex);
        Assert.Contains(vm.Items, n => n.Content == "Ghi chú mới");
        Assert.Equal(string.Empty, vm.Draft);
    }

    [Fact]
    public void Save_EmptyDraft_DoesNothing()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor("doc1");
        vm.Draft = "   ";
        vm.SaveCommand.Execute(null);
        Assert.Empty(store.Rows);
    }

    [Fact]
    public void Save_NoDocumentOpen_DoesNothing()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor(null);
        vm.Draft = "x";
        vm.SaveCommand.Execute(null);
        Assert.Empty(store.Rows);
        Assert.False(vm.CanAddNote);
    }

    [Fact]
    public void Save_WhileEditing_UpdatesInPlace()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 2);
        vm.LoadFor("doc1");
        vm.Draft = "đầu";
        vm.SaveCommand.Execute(null);
        var note = vm.Items.Single();

        vm.BeginEditCommand.Execute(note);
        Assert.True(vm.IsEditing);
        Assert.Equal("đầu", vm.Draft);
        vm.Draft = "đã sửa";
        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("đã sửa", store.Rows[0].Content);
        Assert.Single(vm.Items);
        Assert.Equal("đã sửa", vm.Items[0].Content);
        Assert.False(vm.IsEditing);
    }

    [Fact]
    public void Delete_EditingNote_CancelsEditAndRemoves()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor("doc1");
        vm.Draft = "x";
        vm.SaveCommand.Execute(null);
        var note = vm.Items.Single();
        vm.BeginEditCommand.Execute(note);

        vm.DeleteCommand.Execute(note);

        Assert.Empty(vm.Items);
        Assert.Empty(store.Rows);
        Assert.False(vm.IsEditing);
        Assert.Equal(string.Empty, vm.Draft);
    }

    [Fact]
    public void Open_NoteWithPage_Jumps()
    {
        var store = new FakeNoteStore();
        int? jumped = null;
        var vm = new NotesViewModel(store, () => 7, idx => jumped = idx);
        vm.LoadFor("doc1");
        vm.Draft = "x";
        vm.SaveCommand.Execute(null);
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Equal(7, jumped);
    }

    [Fact]
    public void Open_NoteWithoutPage_DoesNotJump()
    {
        var store = new FakeNoteStore();
        int? jumped = null;
        var vm = new NotesViewModel(store, () => null, idx => jumped = idx);
        vm.LoadFor("doc1");
        vm.Draft = "x";
        vm.SaveCommand.Execute(null); // page null -> note không anchor
        var note = vm.Items.Single();

        vm.OpenCommand.Execute(note);

        Assert.Null(jumped);
    }

    [Fact]
    public void LoadFor_PopulatesAndTogglesCanAdd()
    {
        var store = new FakeNoteStore();
        store.Add(new Note("a", "doc1", "doc1", 1, "có sẵn", 1, 1));
        var vm = Make(store, 1);

        vm.LoadFor("doc1");
        Assert.Single(vm.Items);
        Assert.True(vm.CanAddNote);

        vm.LoadFor(null);
        Assert.Empty(vm.Items);
        Assert.False(vm.CanAddNote);
    }

    [Fact]
    public void Filter_HidesNonMatching_RestoresOnClear()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, 1);
        vm.LoadFor("doc1");
        vm.Draft = "alpha"; vm.SaveCommand.Execute(null);
        vm.Draft = "beta"; vm.SaveCommand.Execute(null);

        vm.FilterText = "alp";
        Assert.Single(vm.Items);
        Assert.Equal("alpha", vm.Items[0].Content);

        vm.FilterText = "";
        Assert.Equal(2, vm.Items.Count);
    }

    [Fact]
    public void MatchesFilter_Rules()
    {
        var n = new Note("a", "o", "o", 1, "Hello World", 1, 1);
        Assert.True(NotesViewModel.MatchesFilter(n, ""));
        Assert.True(NotesViewModel.MatchesFilter(n, "  "));
        Assert.True(NotesViewModel.MatchesFilter(n, "WORLD"));
        Assert.False(NotesViewModel.MatchesFilter(n, "xyz"));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests"`
Expected: FAIL biên dịch (`NotesViewModel` chưa tồn tại).

- [ ] **Step 3: Tạo NotesViewModel**

`src/PdfReaderApp/ViewModels/NotesViewModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Quản lý ghi chú của sách đang mở: danh sách (lọc + sắp trong bộ nhớ, cập nhật tại chỗ),
/// soạn/sửa/xóa, click để nhảy tới trang neo. Tách khỏi MainViewModel để test trọn vẹn.</summary>
public sealed partial class NotesViewModel : ObservableObject
{
    private const int MaxNoteLength = 20000;

    private readonly INoteStore _store;
    private readonly Func<int?> _currentPageIndex;
    private readonly Action<int> _jumpToPageIndex;

    private readonly List<Note> _all = new(); // nguồn đầy đủ; Items là phần đã lọc/sắp
    private string? _ownerKey;
    private string? _editingId;

    public ObservableCollection<Note> Items { get; } = new();

    [ObservableProperty] private string _draft = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _canAddNote;
    [ObservableProperty] private string _statusMessage = string.Empty;

    public NotesViewModel(INoteStore store, Func<int?> currentPageIndex, Action<int> jumpToPageIndex)
    {
        _store = store;
        _currentPageIndex = currentPageIndex;
        _jumpToPageIndex = jumpToPageIndex;
    }

    // Sắp: trang tăng dần (null cuối), rồi tạo mới hơn lên trước.
    private static int CompareNotes(Note a, Note b)
    {
        int pa = a.PageIndex ?? int.MaxValue;
        int pb = b.PageIndex ?? int.MaxValue;
        if (pa != pb) return pa.CompareTo(pb);
        return b.CreatedAtUnixMs.CompareTo(a.CreatedAtUnixMs);
    }

    public static bool MatchesFilter(Note n, string? filter)
        => string.IsNullOrWhiteSpace(filter)
           || n.Content.Contains(filter, StringComparison.OrdinalIgnoreCase);

    public void LoadFor(string? ownerKey)
    {
        CancelEdit();
        _ownerKey = ownerKey;
        CanAddNote = ownerKey != null;
        _all.Clear();
        if (ownerKey != null)
        {
            try { _all.AddRange(_store.GetForOwner(ownerKey)); }
            catch { /* lỗi store không làm hỏng UI */ }
        }
        RebuildItems();
    }

    partial void OnFilterTextChanged(string value) => RebuildItems();

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var n in _all.Where(n => MatchesFilter(n, FilterText)).OrderBy(n => n, Comparer<Note>.Create(CompareNotes)))
            Items.Add(n);
    }

    private void InsertSorted(Note note)
    {
        int i = 0;
        while (i < Items.Count && CompareNotes(note, Items[i]) >= 0) i++;
        Items.Insert(i, note);
    }

    [RelayCommand]
    private void Save()
    {
        StatusMessage = string.Empty;
        string content = (Draft ?? string.Empty).Trim();
        if (content.Length == 0) return;
        if (_ownerKey == null) return;
        if (content.Length > MaxNoteLength)
        {
            StatusMessage = $"Ghi chú quá dài (tối đa {MaxNoteLength} ký tự).";
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_editingId == null)
        {
            var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey,
                _currentPageIndex(), content, now, now);
            try { _store.Add(note); }
            catch { return; }
            _all.Add(note);
            if (MatchesFilter(note, FilterText)) InsertSorted(note);
        }
        else
        {
            int rows;
            try { rows = _store.Update(_editingId, content, now); }
            catch { return; }
            if (rows == 0) { LoadFor(_ownerKey); return; } // note đã bị xóa nơi khác
            int ai = _all.FindIndex(n => n.Id == _editingId);
            if (ai >= 0) _all[ai] = _all[ai] with { Content = content, UpdatedAtUnixMs = now };
            int ii = IndexInItems(_editingId);
            if (ii >= 0)
            {
                if (MatchesFilter(_all[ai], FilterText)) Items[ii] = _all[ai];
                else Items.RemoveAt(ii);
            }
        }

        Draft = string.Empty;
        CancelEdit();
    }

    [RelayCommand]
    private void BeginEdit(Note? note)
    {
        if (note == null) return;
        Draft = note.Content;
        _editingId = note.Id;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        Draft = string.Empty;
        _editingId = null;
        IsEditing = false;
    }

    [RelayCommand]
    private void Delete(Note? note)
    {
        if (note == null) return;
        if (_editingId == note.Id) CancelEdit();
        try { _store.Delete(note.Id); }
        catch { return; }
        _all.RemoveAll(n => n.Id == note.Id);
        int ii = IndexInItems(note.Id);
        if (ii >= 0) Items.RemoveAt(ii);
    }

    [RelayCommand]
    private void Open(Note? note)
    {
        if (note?.PageIndex is int p) _jumpToPageIndex(p);
    }

    private int IndexInItems(string id)
    {
        for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return i;
        return -1;
    }
}
```

- [ ] **Step 4: Chạy test NotesViewModel, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests"`
Expected: PASS 10/10.

- [ ] **Step 5: Nối vào MainViewModel**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`:

5a. Thêm property công khai cạnh các property khác (ví dụ ngay sau `public ObservableCollection<ChatMessage> ChatMessages { get; } = new();`):

```csharp
    public NotesViewModel Notes { get; }
```

5b. Thêm tham số tùy chọn cuối vào ctor inject (sau `IChatHistoryStore? chatHistory = null` đã có từ PR #12):

```csharp
        IChatHistoryStore? chatHistory = null,
        INoteStore? noteStore = null)
```

5c. Trong thân ctor, sau khối dựng `_chatHistory` (`_chatHistory.EnsureSchema();`), thêm:

```csharp
        var notes = noteStore ?? new SqliteNoteStore(System.IO.Path.Combine(AppDir(), "notes.db"));
        notes.EnsureSchema();
        Notes = new NotesViewModel(notes,
            () => _documentId is null ? (int?)null : CurrentPage - 1,
            idx => CurrentPage = idx + 1);
```

5d. Trong `LoadActiveDocument`, nhánh `try` ngay sau dòng `LoadChatHistory();` thêm:

```csharp
            Notes.LoadFor(_documentId);
```

5e. Trong `LoadActiveDocument`, nhánh `catch` ngay sau dòng `LoadChatHistory();` thêm:

```csharp
            Notes.LoadFor(null);
```

- [ ] **Step 6: Build + chạy toàn bộ test**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công.

Run: `dotnet test`
Expected: PASS toàn bộ (số cũ + 8 store + 10 NotesViewModel).

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/ViewModels/NotesViewModel.cs src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs
git commit -m "feat: NotesViewModel (filter/sort/in-place) wired into MainViewModel"
```

---

### Task 3: XAML — sidebar phải thành TabControl (Chat | Notes)

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `Notes` (NotesViewModel) trên DataContext (MainViewModel); các lệnh `Notes.SaveCommand/BeginEditCommand/CancelEditCommand/DeleteCommand/OpenCommand`; thuộc tính `Notes.Items/Draft/FilterText/IsEditing/CanAddNote/StatusMessage`.

**Bối cảnh hiện tại (đọc file trước khi sửa):**
- Sidebar phải là `<materialDesign:Card Grid.Column="3" ... Visibility="{Binding ShowLibrary, Converter={StaticResource InverseBoolToVisibilityConverter}}">` (sau PR #13). Bên trong là một `Grid` 3 hàng: ColorZone "AI ASSISTANT" (hàng 0), `ScrollViewer`+`ItemsControl ChatMessages` (hàng 1), ô nhập chat + nút gửi (hàng 2). Một ItemsControl khác trong file (thẻ thư viện) đã dùng mẫu lệnh `RelativeSource AncestorType=ItemsControl` → tham khảo để bind lệnh note.

- [ ] **Step 1: Bọc nội dung chat hiện có trong TabControl + thêm tab Notes**

Đọc toàn bộ `MainWindow.xaml`. Trong Card sidebar phải (Grid.Column=3), thay `Grid` nội dung hiện tại bằng một `TabControl` style `MaterialDesignTabControl`, đặt nguyên `Grid` chat cũ vào `TabItem` "Chat", và thêm `TabItem` "Notes". Khung như sau (giữ NGUYÊN VĂN nội dung chat cũ vào chỗ đánh dấu):

```xml
<TabControl Style="{StaticResource MaterialDesignTabControl}"
            materialDesign:ColorZoneAssist.Mode="PrimaryMid">
    <TabItem Header="Chat">
        <!-- DÁN NGUYÊN Grid nội dung chat hiện tại vào đây (ColorZone tiêu đề có thể bỏ vì đã có header tab) -->
    </TabItem>
    <TabItem Header="Notes">
        <Grid Margin="8">
            <Grid.RowDefinitions>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="Auto"/>
                <RowDefinition Height="*"/>
            </Grid.RowDefinitions>

            <!-- Ô lọc -->
            <TextBox Grid.Row="0" Margin="0,0,0,8"
                     materialDesign:HintAssist.Hint="Lọc ghi chú"
                     Text="{Binding Notes.FilterText, UpdateSourceTrigger=PropertyChanged}"/>

            <!-- Ô soạn -->
            <TextBox Grid.Row="1" MinHeight="60" Margin="0,0,0,4"
                     AcceptsReturn="True" TextWrapping="Wrap"
                     VerticalScrollBarVisibility="Auto"
                     IsEnabled="{Binding Notes.CanAddNote}"
                     materialDesign:HintAssist.Hint="Viết ghi chú (Ctrl+Enter để lưu)"
                     Text="{Binding Notes.Draft, UpdateSourceTrigger=PropertyChanged}">
                <TextBox.InputBindings>
                    <KeyBinding Modifiers="Control" Key="Enter" Command="{Binding Notes.SaveCommand}"/>
                </TextBox.InputBindings>
            </TextBox>

            <!-- Hàng nút + thông báo -->
            <StackPanel Grid.Row="2" Orientation="Horizontal" Margin="0,0,0,8">
                <Button Content="Lưu" Command="{Binding Notes.SaveCommand}"
                        Style="{StaticResource MaterialDesignFlatButton}"/>
                <Button Content="Hủy" Command="{Binding Notes.CancelEditCommand}"
                        Style="{StaticResource MaterialDesignFlatButton}"
                        Visibility="{Binding Notes.IsEditing, Converter={StaticResource BooleanToVisibilityConverter}}"/>
                <TextBlock Text="{Binding Notes.StatusMessage}" VerticalAlignment="Center"
                           Margin="8,0,0,0" Foreground="{DynamicResource MaterialDesign.Brush.Primary}"/>
            </StackPanel>

            <!-- Danh sách note -->
            <ScrollViewer Grid.Row="3" VerticalScrollBarVisibility="Auto">
                <ItemsControl ItemsSource="{Binding Notes.Items}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <materialDesign:Card Margin="0,0,0,8" Padding="8" UniformCornerRadius="8">
                                <Grid>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>
                                    <Grid.ColumnDefinitions>
                                        <ColumnDefinition Width="*"/>
                                        <ColumnDefinition Width="Auto"/>
                                        <ColumnDefinition Width="Auto"/>
                                    </Grid.ColumnDefinitions>

                                    <!-- Nội dung: click để nhảy trang -->
                                    <TextBlock Grid.Row="0" Grid.Column="0" Text="{Binding Content}"
                                               TextWrapping="Wrap" Cursor="Hand">
                                        <TextBlock.InputBindings>
                                            <MouseBinding MouseAction="LeftClick"
                                                Command="{Binding DataContext.Notes.OpenCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}"/>
                                        </TextBlock.InputBindings>
                                    </TextBlock>

                                    <Button Grid.Row="0" Grid.Column="1" Style="{StaticResource MaterialDesignIconButton}"
                                            Width="28" Height="28" ToolTip="Sửa"
                                            Command="{Binding DataContext.Notes.BeginEditCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding}">
                                        <materialDesign:PackIcon Kind="Pencil" Width="16" Height="16"/>
                                    </Button>
                                    <Button Grid.Row="0" Grid.Column="2" Style="{StaticResource MaterialDesignIconButton}"
                                            Width="28" Height="28" ToolTip="Xóa"
                                            Command="{Binding DataContext.Notes.DeleteCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                            CommandParameter="{Binding}">
                                        <materialDesign:PackIcon Kind="Delete" Width="16" Height="16"/>
                                    </Button>

                                    <!-- Badge trang (PageIndex 0-based -> hiển thị +1); ẩn nếu null -->
                                    <TextBlock Grid.Row="1" Grid.Column="0" Grid.ColumnSpan="3"
                                               Margin="0,4,0,0" FontSize="11" Opacity="0.7"
                                               Text="{Binding PageIndex, Converter={StaticResource PageBadgeConverter}}"
                                               Visibility="{Binding PageIndex, Converter={StaticResource NullToCollapsedConverter}}"/>
                                </Grid>
                            </materialDesign:Card>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Grid>
    </TabItem>
</TabControl>
```

- [ ] **Step 2: Thêm 2 converter nhỏ cho badge trang**

Trong `src/PdfReaderApp/MainWindow.xaml.cs`, thêm 2 converter (giữ dấu tiếng Việt trong comment):

```csharp
// PageIndex (0-based, int?) -> chuỗi "Trang N" (N = index + 1). Null -> rỗng.
public sealed class PageBadgeConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int i ? $"Trang {i + 1}" : string.Empty;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

// Giá trị null -> Collapsed, có giá trị -> Visible (ẩn badge khi note không neo trang).
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
```

Đăng ký 2 converter trong `<Window.Resources>` của `MainWindow.xaml` (cạnh các converter khác đã đăng ký, ví dụ `PathToImageConverter`):

```xml
<local:PageBadgeConverter x:Key="PageBadgeConverter"/>
<local:NullToCollapsedConverter x:Key="NullToCollapsedConverter"/>
```

(Nếu file chưa khai báo namespace `local`, dùng đúng prefix mà các converter hiện có đang dùng — kiểm tra phần `xmlns:` và phần khai báo resource converter sẵn có rồi theo y như vậy.)

- [ ] **Step 3: Build**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công (0 Errors).

- [ ] **Step 4: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml src/PdfReaderApp/MainWindow.xaml.cs
git commit -m "feat: notes tab in right sidebar (TabControl Chat | Notes)"
```

- [ ] **Step 5: Manual GUI verify (người dùng chạy app)**

1. Mở sách → sidebar phải có 2 tab Chat | Notes. Chuyển qua lại: **kiểm tra cuộn chat + focus có bị reset không**; nội dung/stream chat phải còn nguyên. (Nếu reset khó chịu → ghi nhận để áp fix keep-alive template; không làm trước.)
2. Tab Notes: gõ ghi chú, Ctrl+Enter (hoặc nút Lưu) → thẻ note xuất hiện kèm badge "Trang N" = trang đang xem.
3. Sửa (icon bút) → nội dung vào ô soạn, nút Hủy hiện; Lưu → cập nhật tại chỗ, danh sách không nhảy scroll.
4. Xóa (icon thùng rác) → thẻ biến mất.
5. Lọc: gõ vào ô lọc → chỉ còn note khớp; xóa lọc → hiện lại đủ.
6. Click nội dung một note → nhảy tới đúng trang.
7. Ở giao diện thư viện (chưa mở sách): ô soạn note bị khóa (CanAddNote=false).
8. Đổi sang sách khác → danh sách note đổi theo, không lẫn note sách cũ; nếu đang sửa thì thoát chế độ sửa.

---

## Self-Review

**1. Spec coverage:**
- Note model linh hoạt (owner_key + anchor nullable + GUID) → Task 1 model + schema. ✅
- Store SQLite notes.db + user_version + per-op Pooling=False → Task 1. ✅
- NotesViewModel (Items, Draft, IsEditing, FilterText, CanAddNote; Save/BeginEdit/CancelEdit/Delete/Open; LoadFor) → Task 2. ✅
- Cập nhật tại chỗ + lọc + sắp theo trang → Task 2 (InsertSorted/RebuildItems/CompareNotes/MatchesFilter). ✅
- Neo trang hiện tại lúc tạo + click nhảy trang → Task 2 (currentPageIndex, Open) + Task 3 badge/MouseBinding. ✅
- Nối MainViewModel + ctor tùy chọn noteStore + LoadFor khi mở/đổi sách → Task 2 Step 5. ✅
- TabControl MD Chat | Notes, giữ Visibility/width sidebar → Task 3. ✅
- Composer khóa khi chưa mở sách, Ctrl+Enter lưu, guard rỗng/dài, xóa-khi-sửa, update-trúng-0-dòng, chốt ownerKey lúc LoadFor → Task 2 (logic) + Task 3 (IsEnabled, KeyBinding). ✅
- Lỗi store nuốt ở VM → Task 2 (try/catch trong LoadFor/Save/Delete). ✅

**2. Placeholder scan:** Không có TBD/TODO; mọi step code/lệnh cụ thể. Task 3 Step 1 có chỗ "DÁN NGUYÊN Grid chat cũ" — đây là thao tác di chuyển nguyên khối XAML hiện có (không phải nội dung thiếu), kèm khung TabControl đầy đủ.

**3. Type consistency:** `Note(Id, OwnerKey, DocumentId, PageIndex, Content, CreatedAtUnixMs, UpdatedAtUnixMs)`; `INoteStore.{EnsureSchema,Add,Update(string,string,long):int,Delete(string):int,GetForOwner(string)}`; `NotesViewModel(INoteStore, Func<int?>, Action<int>)` + `Notes` property + ctor param `INoteStore? noteStore = null`; lệnh sinh tên `SaveCommand/BeginEditCommand/CancelEditCommand/DeleteCommand/OpenCommand` — nhất quán giữa Task 1/2/3 và test.
