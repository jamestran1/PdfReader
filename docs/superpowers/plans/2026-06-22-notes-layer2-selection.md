# Notes Layer 2b — Chọn text → note trích dẫn — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bôi đen text trên trang (theo dòng chữ) → nút nổi "Thêm ghi chú" → mở tab Notes với trích dẫn gắn sẵn, neo trang chứa đoạn chọn.

**Architecture:** Logic chọn tách lớp thuần `TextSelectionResolver` (Core, test được). `Note` thêm trường `Quote` (migrate SQLite user_version 2). `NotesViewModel` nhận trích dẫn chờ (PendingQuote) + chuyển tab. `PdfViewerControl` thêm kéo-chọn + overlay + nút nổi, đẩy ra `MainViewModel` qua DependencyProperty command.

**Tech Stack:** WPF .NET 10, CommunityToolkit.Mvvm, MaterialDesignThemes 5.1.0, SkiaSharp, PdfiumViewer, Microsoft.Data.Sqlite, xUnit.

## Global Constraints

- Comment/chuỗi UI tiếng Việt GIỮ DẤU. Không dùng dấu gạch ngang dài (em dash).
- Chọn trong MỘT trang (v1); giữ nguyên pan/scroll, double-click ghost, Ctrl+wheel zoom.
- Quote lưu ở trường riêng `quote` (nullable); migrate bằng `ALTER TABLE ... ADD COLUMN` chỉ khi cột thiếu (kiểm qua `PRAGMA table_info`), set `PRAGMA user_version = 2`.
- Note neo vào trang CHỨA đoạn chọn (không phải trang giữa viewport).
- 2b KHÔNG lưu/vẽ lại highlight bền vững trên trang (đó là 2c); highlight lúc chọn chỉ là tạm.
- Note record (sau task 2): `Note(string Id, string OwnerKey, string? DocumentId, int? PageIndex, string? Quote, string Content, long CreatedAtUnixMs, long UpdatedAtUnixMs)` — Quote chèn giữa PageIndex và Content.
- Store calls trong VM bọc try/catch nuốt lỗi (như Layer 1).

---

### Task 1: TextSelectionResolver (Core, thuần)

**Files:**
- Create: `src/PdfReaderApp/Core/TextSelectionResolver.cs`
- Test: `tests/PdfReaderApp.Tests/Core/TextSelectionResolverTests.cs`

**Interfaces:**
- Produces:
  - `readonly record struct SelChar(int CharIndex, string Text, System.Windows.Rect Bounds)`
  - `sealed record SelectionResult(string Text, System.Collections.Generic.IReadOnlyList<System.Windows.Rect> LineRects)`
  - `static class TextSelectionResolver` với `SelectionResult Resolve(IReadOnlyList<SelChar> chars, int anchorIndex, int focusIndex)` và `int NearestCharIndex(IReadOnlyList<SelChar> chars, System.Windows.Point p)` (trả CharIndex gần nhất, -1 nếu rỗng).

- [ ] **Step 1: Viết test thất bại**

Tạo `tests/PdfReaderApp.Tests/Core/TextSelectionResolverTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Core;

public class TextSelectionResolverTests
{
    // Hàng 1: "AB" ở y=0; hàng 2: "CD" ở y=20. Mỗi ký tự rộng 10, cao 10.
    private static List<SelChar> Sample() => new()
    {
        new SelChar(0, "A", new Rect(0, 0, 10, 10)),
        new SelChar(1, "B", new Rect(10, 0, 10, 10)),
        new SelChar(2, "C", new Rect(0, 20, 10, 10)),
        new SelChar(3, "D", new Rect(10, 20, 10, 10)),
    };

    [Fact]
    public void Resolve_ForwardRange_JoinsTextInReadingOrder()
    {
        var r = TextSelectionResolver.Resolve(Sample(), 0, 2);
        Assert.Equal("ABC", r.Text);
    }

    [Fact]
    public void Resolve_ReversedRange_SameAsForward()
    {
        var fwd = TextSelectionResolver.Resolve(Sample(), 0, 3);
        var rev = TextSelectionResolver.Resolve(Sample(), 3, 0);
        Assert.Equal(fwd.Text, rev.Text);
        Assert.Equal("ABCD", fwd.Text);
    }

    [Fact]
    public void Resolve_SingleChar_WhenAnchorEqualsFocus()
    {
        var r = TextSelectionResolver.Resolve(Sample(), 1, 1);
        Assert.Equal("B", r.Text);
        Assert.Single(r.LineRects);
    }

    [Fact]
    public void Resolve_TwoLines_ProducesTwoLineRects()
    {
        var r = TextSelectionResolver.Resolve(Sample(), 0, 3);
        Assert.Equal(2, r.LineRects.Count);
        // Hàng 1 gộp A+B -> rộng 20 ở y=0; hàng 2 gộp C+D -> y=20.
        Assert.Contains(r.LineRects, rc => rc.Top == 0 && rc.Width == 20);
        Assert.Contains(r.LineRects, rc => rc.Top == 20 && rc.Width == 20);
    }

    [Fact]
    public void Resolve_Empty_ReturnsEmpty()
    {
        var r = TextSelectionResolver.Resolve(new List<SelChar>(), 0, 0);
        Assert.Equal("", r.Text);
        Assert.Empty(r.LineRects);
    }

    [Fact]
    public void NearestCharIndex_PointInsideChar_ReturnsThatChar()
    {
        Assert.Equal(3, TextSelectionResolver.NearestCharIndex(Sample(), new Point(12, 22)));
    }

    [Fact]
    public void NearestCharIndex_PointOutside_ReturnsClosest()
    {
        // Xa bên phải hàng 1 -> gần B (index 1).
        Assert.Equal(1, TextSelectionResolver.NearestCharIndex(Sample(), new Point(100, 2)));
    }

    [Fact]
    public void NearestCharIndex_Empty_ReturnsMinusOne()
    {
        Assert.Equal(-1, TextSelectionResolver.NearestCharIndex(new List<SelChar>(), new Point(0, 0)));
    }
}
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~TextSelectionResolverTests"`
Expected: FAIL biên dịch (`TextSelectionResolver` chưa tồn tại).

- [ ] **Step 3: Triển khai**

`src/PdfReaderApp/Core/TextSelectionResolver.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;

namespace PdfReaderApp.Core;

public readonly record struct SelChar(int CharIndex, string Text, Rect Bounds);

public sealed record SelectionResult(string Text, IReadOnlyList<Rect> LineRects);

/// <summary>Tính vùng chọn text theo dòng chữ: từ dải [anchor,focus] (theo CharIndex) trên
/// một trang -> chuỗi text theo thứ tự đọc + các rect gộp theo dòng. Thuần, không phụ thuộc view.</summary>
public static class TextSelectionResolver
{
    public static SelectionResult Resolve(IReadOnlyList<SelChar> chars, int anchorIndex, int focusIndex)
    {
        if (chars == null || chars.Count == 0)
            return new SelectionResult(string.Empty, Array.Empty<Rect>());

        int lo = Math.Min(anchorIndex, focusIndex);
        int hi = Math.Max(anchorIndex, focusIndex);

        var selected = chars.Where(c => c.CharIndex >= lo && c.CharIndex <= hi)
                            .OrderBy(c => c.CharIndex)
                            .ToList();
        if (selected.Count == 0)
            return new SelectionResult(string.Empty, Array.Empty<Rect>());

        var sb = new StringBuilder();
        foreach (var c in selected) sb.Append(c.Text);

        // Gộp rect theo dòng: cùng dòng nếu chênh lệch tâm-Y nhỏ hơn nửa chiều cao ký tự.
        var lines = new List<Rect>();
        Rect current = selected[0].Bounds;
        double lineCenterY = current.Top + current.Height / 2;
        for (int i = 1; i < selected.Count; i++)
        {
            var b = selected[i].Bounds;
            double cy = b.Top + b.Height / 2;
            if (Math.Abs(cy - lineCenterY) <= b.Height / 2)
            {
                current = Rect.Union(current, b);
            }
            else
            {
                lines.Add(current);
                current = b;
                lineCenterY = cy;
            }
        }
        lines.Add(current);

        return new SelectionResult(sb.ToString(), lines);
    }

    public static int NearestCharIndex(IReadOnlyList<SelChar> chars, Point p)
    {
        if (chars == null || chars.Count == 0) return -1;
        int best = -1;
        double bestDist = double.MaxValue;
        foreach (var c in chars)
        {
            double dx = p.X < c.Bounds.Left ? c.Bounds.Left - p.X
                      : p.X > c.Bounds.Right ? p.X - c.Bounds.Right : 0;
            double dy = p.Y < c.Bounds.Top ? c.Bounds.Top - p.Y
                      : p.Y > c.Bounds.Bottom ? p.Y - c.Bounds.Bottom : 0;
            double d = dx * dx + dy * dy;
            if (d < bestDist) { bestDist = d; best = c.CharIndex; }
        }
        return best;
    }
}
```

- [ ] **Step 4: Chạy test, xác nhận đạt**

Run: `dotnet test --filter "FullyQualifiedName~TextSelectionResolverTests"`
Expected: PASS 8/8.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Core/TextSelectionResolver.cs tests/PdfReaderApp.Tests/Core/TextSelectionResolverTests.cs
git commit -m "feat: TextSelectionResolver (range to text + line rects, nearest char)"
```

---

### Task 2: Note.Quote + migrate SqliteNoteStore (user_version 2)

**Files:**
- Modify: `src/PdfReaderApp/Models/Note.cs`
- Modify: `src/PdfReaderApp/Services/SqliteNoteStore.cs`
- Modify: `src/PdfReaderApp/ViewModels/NotesViewModel.cs` (chỉ sửa chỗ tạo `Note` để build xanh; logic quote thật ở Task 3)
- Test: `tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs` (thêm test), `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs` (sửa chỗ tạo Note)

**Interfaces:**
- Produces: `Note` có thêm `string? Quote` (vị trí thứ 5, giữa `PageIndex` và `Content`). `SqliteNoteStore` đọc/ghi `quote`, schema version 2.

- [ ] **Step 1: Viết test thất bại (quote round-trip + migrate)**

Trong `tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs`: cập nhật helper `N` để có quote null, và thêm 2 test. Sửa helper:

```csharp
    private static Note N(string id, string owner, int? page, string content, long t) =>
        new(id, owner, owner, page, null, content, t, t);
```

Thêm vào lớp:

```csharp
    [Fact]
    public void Add_WithQuote_RoundTrips()
    {
        _store.Add(new Note("q1", "docA", "docA", 2, "đoạn trích", "bình luận", 10, 10));
        var got = _store.GetForOwner("docA").Single();
        Assert.Equal("đoạn trích", got.Quote);
        Assert.Equal("bình luận", got.Content);
    }

    [Fact]
    public void EnsureSchema_MigratesV1DbByAddingQuoteColumn()
    {
        string db = Path.Combine(_dir, "v1.db");
        // Tạo bảng kiểu v1 (KHÔNG có cột quote), user_version chưa set, có 1 dòng.
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var c = conn.CreateCommand();
            c.CommandText = @"CREATE TABLE note (id TEXT PRIMARY KEY, owner_key TEXT NOT NULL, document_id TEXT,
                page_index INTEGER, content TEXT NOT NULL, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
                INSERT INTO note (id, owner_key, document_id, page_index, content, created_at, updated_at)
                VALUES ('old','docA','docA',1,'cũ',1,1);";
            c.ExecuteNonQuery();
        }

        var store = new SqliteNoteStore(db);
        store.EnsureSchema(); // phải thêm cột quote, set user_version=2, giữ dữ liệu cũ

        var got = store.GetForOwner("docA").Single();
        Assert.Equal("cũ", got.Content);
        Assert.Null(got.Quote);

        // ghi note có quote vào db đã migrate -> đọc lại được
        store.Add(new Note("new", "docA", "docA", 1, "trích", "mới", 2, 2));
        Assert.Contains(store.GetForOwner("docA"), n => n.Quote == "trích");
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~SqliteNoteStoreTests"`
Expected: FAIL biên dịch (`Note` chưa có Quote).

- [ ] **Step 3: Cập nhật model**

`src/PdfReaderApp/Models/Note.cs` — thêm `Quote`:

```csharp
namespace PdfReaderApp.Models;

/// <summary>Một ghi chú. OwnerKey = phạm vi (v1 = documentId), DocumentId = anchor.
/// Quote = đoạn trích dẫn bôi đen từ trang (nullable; null với note tự do).</summary>
public sealed record Note(
    string Id,
    string OwnerKey,
    string? DocumentId,
    int? PageIndex,
    string? Quote,
    string Content,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs);
```

- [ ] **Step 4: Cập nhật SqliteNoteStore (schema v2 + quote)**

Trong `src/PdfReaderApp/Services/SqliteNoteStore.cs`:

4a. `private const long SchemaVersion = 2;`

4b. `EnsureSchema` — CREATE gồm `quote`, và migrate db cũ nếu thiếu cột:

```csharp
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
  quote TEXT,
  content TEXT NOT NULL,
  created_at INTEGER NOT NULL,
  updated_at INTEGER NOT NULL);
CREATE INDEX IF NOT EXISTS ix_note_owner ON note(owner_key);";
            cmd.ExecuteNonQuery();

            // Db cũ (v1) tạo trước khi có cột quote: thêm cột nếu thiếu.
            if (!ColumnExists(conn, "note", "quote"))
            {
                using var alter = conn.CreateCommand();
                alter.CommandText = "ALTER TABLE note ADD COLUMN quote TEXT;";
                alter.ExecuteNonQuery();
            }

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

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        using var r = cmd.ExecuteReader();
        while (r.Read())
            if (string.Equals(r.GetString(1), column, System.StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }
```

4c. `Add` — chèn quote:

```csharp
    public void Add(Note note)
    {
        lock (_lock)
        {
            using var conn = OpenConn();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO note (id, owner_key, document_id, page_index, quote, content, created_at, updated_at)
VALUES ($id, $owner, $doc, $page, $quote, $content, $created, $updated);";
            cmd.Parameters.AddWithValue("$id", note.Id);
            cmd.Parameters.AddWithValue("$owner", note.OwnerKey);
            cmd.Parameters.AddWithValue("$doc", (object?)note.DocumentId ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$page", (object?)note.PageIndex ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$quote", (object?)note.Quote ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$content", note.Content);
            cmd.Parameters.AddWithValue("$created", note.CreatedAtUnixMs);
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAtUnixMs);
            cmd.ExecuteNonQuery();
        }
    }
```

4d. `GetForOwner` — đọc quote (cột thứ 5):

```csharp
            cmd.CommandText = "SELECT id, owner_key, document_id, page_index, quote, content, created_at, updated_at FROM note WHERE owner_key=$owner";
            ...
            while (r.Read())
            {
                list.Add(new Note(
                    r.GetString(0),
                    r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5),
                    r.GetInt64(6),
                    r.GetInt64(7)));
            }
```

(`Update`/`Delete` giữ nguyên.)

- [ ] **Step 5: Giữ build xanh ở NotesViewModel (tạm thời)**

Trong `src/PdfReaderApp/ViewModels/NotesViewModel.cs`, chỗ tạo `Note` trong `Save` (đường thêm mới) — chèn `null` cho Quote để khớp record mới (logic quote thật làm ở Task 3):

```csharp
            var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey,
                _currentPageIndex(), null, content, now, now);
```

Trong `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`, sửa chỗ tạo `Note` (test `LoadFor_PopulatesAndTogglesCanAdd` và bất kỳ chỗ `new Note(...)` positional nào) thêm `null` quote đúng vị trí, ví dụ:

```csharp
        store.Add(new Note("a", "doc1", "doc1", 1, null, "có sẵn", 1, 1));
```

(`MatchesFilter_Rules` test cũng tạo `new Note(...)` — thêm `null` quote: `new Note("a","o","o",1,null,"Hello World",1,1)`.)

- [ ] **Step 6: Build + test**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công.

Run: `dotnet test`
Expected: PASS toàn bộ (gồm 2 test store mới + các test cũ đã sửa Note).

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/Models/Note.cs src/PdfReaderApp/Services/SqliteNoteStore.cs src/PdfReaderApp/ViewModels/NotesViewModel.cs tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs
git commit -m "feat: Note.Quote column + SQLite migration to user_version 2"
```

---

### Task 3: NotesViewModel quote/tab + NoteSelection + MainViewModel command

**Files:**
- Create: `src/PdfReaderApp/Models/NoteSelection.cs`
- Modify: `src/PdfReaderApp/ViewModels/NotesViewModel.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`

**Interfaces:**
- Consumes: `Note.Quote` (Task 2).
- Produces:
  - `sealed record NoteSelection(string Quote, int PageIndex)` (namespace `PdfReaderApp.Models`).
  - `NotesViewModel`: `[ObservableProperty] int RightTabIndex`, `[ObservableProperty] string? PendingQuote`, `void BeginNoteFromSelection(string quote, int pageIndex)`; `Save` gắn quote chờ.
  - `MainViewModel`: `[RelayCommand] void AddNoteFromSelection(NoteSelection? sel)`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`:

```csharp
    [Fact]
    public void BeginNoteFromSelection_SetsPendingQuoteAndSwitchesTab()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9);
        vm.LoadFor("doc1");

        vm.BeginNoteFromSelection("đoạn trích", 4);

        Assert.Equal("đoạn trích", vm.PendingQuote);
        Assert.Equal(1, vm.RightTabIndex);
    }

    [Fact]
    public void Save_WithPendingQuote_UsesSelectionPageAndStoresQuote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 9); // trang hiện tại 9, nhưng đoạn chọn ở trang 4
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("đoạn trích", 4);
        vm.Draft = "ý của tôi";

        vm.SaveCommand.Execute(null);

        var saved = store.Rows.Single();
        Assert.Equal("đoạn trích", saved.Quote);
        Assert.Equal(4, saved.PageIndex);          // dùng trang đoạn chọn, không phải 9
        Assert.Equal("ý của tôi", saved.Content);
        Assert.Null(vm.PendingQuote);              // pending xóa sau lưu
    }

    [Fact]
    public void Save_QuoteOnly_EmptyDraft_StillCreatesNote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("chỉ trích dẫn", 0);
        // Draft để rỗng

        vm.SaveCommand.Execute(null);

        Assert.Single(store.Rows);
        Assert.Equal("chỉ trích dẫn", store.Rows[0].Quote);
        Assert.Equal("", store.Rows[0].Content);
    }

    [Fact]
    public void CancelEdit_ClearsPendingQuote()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("trích", 0);
        vm.CancelEditCommand.Execute(null);
        Assert.Null(vm.PendingQuote);
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests"`
Expected: FAIL biên dịch (`BeginNoteFromSelection`/`PendingQuote`/`RightTabIndex` chưa có).

- [ ] **Step 3: Tạo NoteSelection model**

`src/PdfReaderApp/Models/NoteSelection.cs`:

```csharp
namespace PdfReaderApp.Models;

/// <summary>Kết quả một vùng chọn text trên trang để tạo note: đoạn trích + trang chứa nó (0-based).</summary>
public sealed record NoteSelection(string Quote, int PageIndex);
```

- [ ] **Step 4: Mở rộng NotesViewModel**

Trong `src/PdfReaderApp/ViewModels/NotesViewModel.cs`:

4a. Thêm field/property (cạnh các `[ObservableProperty]` hiện có):

```csharp
    [ObservableProperty] private int _rightTabIndex;
    [ObservableProperty] private string? _pendingQuote;
    private int _pendingPageIndex;
```

4b. Thêm method:

```csharp
    // Bắt đầu tạo note từ vùng chọn: chuyển sang tab Notes, giữ trích dẫn + trang chờ.
    public void BeginNoteFromSelection(string quote, int pageIndex)
    {
        CancelEdit();
        PendingQuote = quote;
        _pendingPageIndex = pageIndex;
        RightTabIndex = 1; // 0=Chat, 1=Notes
    }
```

4c. Sửa `CancelEdit` để xóa pending:

```csharp
    [RelayCommand]
    private void CancelEdit()
    {
        Draft = string.Empty;
        _editingId = null;
        IsEditing = false;
        PendingQuote = null;
    }
```

4d. Sửa `LoadFor` xóa pending: thêm `PendingQuote = null;` ngay sau `CancelEdit();` đầu hàm (CancelEdit đã xóa, nhưng để rõ ràng giữ một dòng; nếu CancelEdit đã set null thì bỏ qua bước này).

4e. Sửa `Save` để dùng pending quote + cho phép Draft rỗng khi có quote, và dùng trang đoạn chọn:

```csharp
    [RelayCommand]
    private void Save()
    {
        StatusMessage = string.Empty;
        string content = (Draft ?? string.Empty).Trim();
        bool hasQuote = !string.IsNullOrEmpty(PendingQuote);
        if (content.Length == 0 && !hasQuote) return;   // cho phép quote-only
        if (_ownerKey == null) return;
        if (content.Length > MaxNoteLength)
        {
            StatusMessage = $"Ghi chú quá dài (tối đa {MaxNoteLength} ký tự).";
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        if (_editingId == null)
        {
            int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();
            var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey,
                page, PendingQuote, content, now, now);
            try { _store.Add(note); }
            catch { return; }
            _all.Add(note);
            if (MatchesFilter(note, FilterText)) InsertSorted(note);
        }
        else
        {
            // ... giữ NGUYÊN nhánh update hiện có (không gắn quote khi sửa) ...
        }

        Draft = string.Empty;
        CancelEdit();
    }
```

(Giữ nguyên toàn bộ nhánh `else` (update) như cũ. `CancelEdit()` ở cuối đã xóa PendingQuote.)

4f. `MatchesFilter` cũng nên khớp trên Quote (tùy chọn nhỏ, hữu ích): đổi thành khớp Content HOẶC Quote:

```csharp
    public static bool MatchesFilter(Note n, string? filter)
        => string.IsNullOrWhiteSpace(filter)
           || n.Content.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || (n.Quote != null && n.Quote.Contains(filter, StringComparison.OrdinalIgnoreCase));
```

- [ ] **Step 5: Thêm command vào MainViewModel**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`, thêm (cạnh các RelayCommand khác):

```csharp
    [RelayCommand]
    private void AddNoteFromSelection(PdfReaderApp.Models.NoteSelection? sel)
    {
        if (sel is null) return;
        Notes.BeginNoteFromSelection(sel.Quote, sel.PageIndex);
    }
```

- [ ] **Step 6: Build + test**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công.

Run: `dotnet test`
Expected: PASS toàn bộ (gồm 4 test NotesViewModel mới).

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/Models/NoteSelection.cs src/PdfReaderApp/ViewModels/NotesViewModel.cs src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs
git commit -m "feat: NotesViewModel quote-from-selection flow + AddNoteFromSelection command"
```

---

### Task 4: PdfViewerControl kéo-chọn + overlay + nút nổi + XAML

**Files:**
- Modify: `src/PdfReaderApp/Core/PdfObjectManager.cs` (thêm accessor danh sách ký tự)
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml` + `.cs`
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `TextSelectionResolver` (Task 1), `NoteSelection` (Task 3), `MainViewModel.AddNoteFromSelectionCommand` (Task 3), `Notes.RightTabIndex`/`Notes.PendingQuote` (Task 3).
- Produces: `PdfViewerControl.AddNoteFromSelectionCommand` (DependencyProperty, ICommand); `PdfObjectManager.GetPageTexts(int)`.

**Bối cảnh (đọc file trước khi sửa):**
- `PdfViewerControl.xaml.cs`: `OnCanvasMouseDown` (~242, hiện chỉ xử lý double-click), `HandleDoubleClick` (~255) có mẫu lặp `_slots` + đổi screen→PDF `pdfX=(p.X-rect.Left)/scale`; trường `_currentDocument`, `_objectManager` (PdfObjectManager), `_slots`, `ZoomLevel`; overlay vẽ ở `DrawHighlights` (~510) dùng `PdfCoordinateMapper`; `InteractionCanvas` là overlay trong suốt trong `.xaml`.
- `MainWindow.xaml`: `PdfViewer` là `<controls:PdfViewerControl .../>`; sidebar phải là `TabControl` (Chat | Notes); ô soạn + thẻ note ở tab Notes (từ Layer 1).

- [ ] **Step 1: Thêm accessor danh sách ký tự cho PdfObjectManager**

Trong `src/PdfReaderApp/Core/PdfObjectManager.cs` thêm:

```csharp
    public IReadOnlyList<GhostText> GetPageTexts(int pageIndex)
        => _pageTextMap.TryGetValue(pageIndex, out var g) ? g : System.Array.Empty<GhostText>();
```

- [ ] **Step 2: Thêm DependencyProperty command + nút nổi vào PdfViewerControl**

2a. `.cs` — DP:

```csharp
    public static readonly DependencyProperty AddNoteFromSelectionCommandProperty =
        DependencyProperty.Register(nameof(AddNoteFromSelectionCommand), typeof(System.Windows.Input.ICommand),
            typeof(PdfViewerControl), new PropertyMetadata(null));

    public System.Windows.Input.ICommand? AddNoteFromSelectionCommand
    {
        get => (System.Windows.Input.ICommand?)GetValue(AddNoteFromSelectionCommandProperty);
        set => SetValue(AddNoteFromSelectionCommandProperty, value);
    }
```

2b. `.xaml` — trong `InteractionCanvas`, thêm nút nổi (ẩn mặc định):

```xml
<Button x:Name="AddNoteButton" Visibility="Collapsed" Panel.ZIndex="100"
        Style="{StaticResource MaterialDesignRaisedButton}"
        Content="Thêm ghi chú" Padding="8,2" FontSize="12"
        Click="AddNoteButton_Click"/>
```

- [ ] **Step 3: Trạng thái + xử lý chọn trong PdfViewerControl.cs**

Thêm trường + handlers. Đăng ký `MouseMove`/`MouseLeftButtonUp` (và dùng `OnCanvasMouseDown` sẵn có cho left-down khi không phải double-click):

```csharp
    // Trạng thái chọn text
    private int _selPageIndex = -1;
    private int _anchorChar = -1;
    private bool _selecting;
    private string _selectionText = string.Empty;
    private readonly List<Rect> _selectionRectsPdf = new(); // theo PDF points của _selPageIndex

    private List<SelChar> BuildSelChars(int pageIndex)
    {
        var list = new List<SelChar>();
        foreach (var g in _objectManager.GetPageTexts(pageIndex))
            list.Add(new SelChar(g.CharIndex, g.Text, g.Bounds));
        return list;
    }

    private bool TryPageHit(Point screenPoint, out PageSlot slot, out Point pdfPoint)
    {
        float scale = (float)ZoomLevel;
        foreach (var s in _slots)
        {
            var rect = new Rect(s.X, s.Y, s.Width, s.Height);
            if (rect.Contains(screenPoint))
            {
                slot = s;
                pdfPoint = new Point((screenPoint.X - rect.Left) / scale, (screenPoint.Y - rect.Top) / scale);
                return true;
            }
        }
        slot = default!; pdfPoint = default; return false;
    }
```

Trong `OnCanvasMouseDown`, sau nhánh double-click, thêm xử lý bắt đầu chọn (single left-down):

```csharp
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 1 && _currentDocument != null)
        {
            ClearSelection();
            var sp = e.GetPosition(InteractionCanvas);
            if (TryPageHit(sp, out var slot, out var pdf))
            {
                _objectManager.MapPage(_currentDocument.Pages[slot.PageIndex], slot.PageIndex); // bảo đảm đã map
                var chars = BuildSelChars(slot.PageIndex);
                int anchor = TextSelectionResolver.NearestCharIndex(chars, pdf);
                if (anchor >= 0)
                {
                    _selPageIndex = slot.PageIndex;
                    _anchorChar = anchor;
                    _selecting = true;
                    InteractionCanvas.CaptureMouse();
                }
            }
        }
```

Thêm `MouseMove` + `MouseLeftButtonUp` (đăng ký trong ctor hoặc XAML của InteractionCanvas):

```csharp
    private void InteractionCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_selecting || _selPageIndex < 0) return;
        var sp = e.GetPosition(InteractionCanvas);
        if (!TryPageHit(sp, out var slot, out var pdf) || slot.PageIndex != _selPageIndex) return;
        var chars = BuildSelChars(_selPageIndex);
        int focus = TextSelectionResolver.NearestCharIndex(chars, pdf);
        if (focus < 0) return;
        var res = TextSelectionResolver.Resolve(chars, _anchorChar, focus);
        _selectionText = res.Text;
        _selectionRectsPdf.Clear();
        _selectionRectsPdf.AddRange(res.LineRects);
        AddNoteButton.Visibility = Visibility.Collapsed;
        RedrawOverlay(); // xem Step 4
    }

    private void InteractionCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_selecting) return;
        _selecting = false;
        InteractionCanvas.ReleaseMouseCapture();
        if (string.IsNullOrEmpty(_selectionText) || _selectionRectsPdf.Count == 0) { return; }
        // Đặt nút nổi gần cuối vùng chọn (đổi PDF -> screen của trang _selPageIndex).
        var slot = _slots.FirstOrDefault(s => s.PageIndex == _selPageIndex);
        if (slot == null) return;
        float scale = (float)ZoomLevel;
        var last = _selectionRectsPdf[_selectionRectsPdf.Count - 1];
        Canvas.SetLeft(AddNoteButton, slot.X + last.Right * scale);
        Canvas.SetTop(AddNoteButton, slot.Y + last.Bottom * scale + 2);
        AddNoteButton.Visibility = Visibility.Visible;
    }

    private void AddNoteButton_Click(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_selectionText) && _selPageIndex >= 0)
        {
            var sel = new PdfReaderApp.Models.NoteSelection(_selectionText, _selPageIndex);
            if (AddNoteFromSelectionCommand?.CanExecute(sel) == true)
                AddNoteFromSelectionCommand.Execute(sel);
        }
        ClearSelection();
    }

    private void ClearSelection()
    {
        _selecting = false;
        _selPageIndex = -1;
        _anchorChar = -1;
        _selectionText = string.Empty;
        _selectionRectsPdf.Clear();
        AddNoteButton.Visibility = Visibility.Collapsed;
        RedrawOverlay();
    }
```

Đăng ký sự kiện InteractionCanvas (trong `.xaml` của InteractionCanvas thêm `MouseMove="InteractionCanvas_MouseMove" MouseLeftButtonUp="InteractionCanvas_MouseLeftButtonUp"`), và xử lý Esc: trong control, bắt `KeyDown` (hoặc PreviewKeyDown của control) gọi `ClearSelection()` khi `e.Key == Key.Escape`. Khi đổi trang/cuộn (ScrollChanged hiện có) gọi `ClearSelection()`.

- [ ] **Step 4: Vẽ overlay vùng chọn (Skia)**

Đọc hàm vẽ overlay chứa `DrawHighlights` (~510) và nơi gọi nó (hàm paint của SKElement). Thêm vẽ `_selectionRectsPdf` cho trang `_selPageIndex` bằng cùng `PdfCoordinateMapper` như `DrawHighlights`, paint màu xanh mờ:

```csharp
    // Trong hàm paint overlay, sau khi vẽ search-highlights cho từng slot:
    if (_selPageIndex >= 0 && _selectionRectsPdf.Count > 0)
    {
        var slot = _slots.FirstOrDefault(s => s.PageIndex == _selPageIndex);
        if (slot != null)
        {
            float scale = (float)ZoomLevel;
            double pageHeightPt = _currentDocument!.Pages[_selPageIndex].Height; // pt
            var mapper = new Core.PdfCoordinateMapper(pageHeightPt, scale, 72);
            using var paint = new SKPaint { Color = new SKColor(33, 150, 243, 90), Style = SKPaintStyle.Fill };
            foreach (var r in _selectionRectsPdf)
            {
                var tl = mapper.PdfPointToRender(r.Left, r.Top);
                var br = mapper.PdfPointToRender(r.Right, r.Bottom);
                float x = slot.X != 0 ? (float)(slot.X) : 0; // dùng đúng offset slot như DrawHighlights
                // Tịnh tiến theo vị trí slot trên canvas giống cách DrawHighlights nhận pageRect.
                canvas.DrawRect(SKRect.Create((float)(slot.X + Math.Min(tl.X, br.X)),
                                              (float)(slot.Y + Math.Min(tl.Y, br.Y)),
                                              (float)Math.Abs(br.X - tl.X),
                                              (float)Math.Abs(br.Y - tl.Y)), paint);
            }
        }
    }
```

Lưu ý: căn cách dịch tọa độ theo ĐÚNG cách `DrawHighlights` đang làm (nó nhận `pageRect` + dùng mapper). Mô phỏng y hệt để khớp. `RedrawOverlay()` = gọi `InvalidateVisual()` của SKElement (hoặc tên hàm refresh overlay hiện có — đọc code để dùng đúng).

- [ ] **Step 5: Nối XAML (MainWindow.xaml)**

5a. Bind DP command của viewer:

```xml
<controls:PdfViewerControl x:Name="PdfViewer" ...
    AddNoteFromSelectionCommand="{Binding AddNoteFromSelectionCommand}" ... />
```

5b. `TabControl` (sidebar phải) bind chọn tab:

```xml
<TabControl ... SelectedIndex="{Binding Notes.RightTabIndex, Mode=TwoWay}">
```

5c. Banner trích dẫn trên ô soạn tab Notes (đặt trên TextBox Draft):

```xml
<Border BorderBrush="{DynamicResource MaterialDesignDivider}" BorderThickness="3,0,0,0"
        Padding="6,2" Margin="0,0,0,4"
        Visibility="{Binding Notes.PendingQuote, Converter={StaticResource NullToCollapsedConverter}}">
    <TextBlock Text="{Binding Notes.PendingQuote}" FontStyle="Italic" Opacity="0.8"
               TextTrimming="CharacterEllipsis" MaxHeight="48" TextWrapping="Wrap"/>
</Border>
```

5d. Thẻ note: thêm khối Quote phía trên Content (ẩn nếu null), cắt gọn ~3 dòng:

```xml
<Border BorderBrush="{DynamicResource MaterialDesignDivider}" BorderThickness="3,0,0,0"
        Padding="6,0" Margin="0,0,0,4"
        Visibility="{Binding Quote, Converter={StaticResource NullToCollapsedConverter}}">
    <TextBlock Text="{Binding Quote}" FontStyle="Italic" Opacity="0.75"
               TextWrapping="Wrap" MaxHeight="60" TextTrimming="CharacterEllipsis"/>
</Border>
```

(`NullToCollapsedConverter` đã đăng ký từ Layer 1.)

- [ ] **Step 6: Build**

Run: `dotnet build PdfReaderApp.slnx`
Expected: build thành công (0 Errors).

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/Core/PdfObjectManager.cs src/PdfReaderApp/Controls/PdfViewerControl.xaml src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: drag text selection on page -> add-note button -> quote note"
```

- [ ] **Step 8: Manual GUI verify (người dùng chạy app)**

1. Mở sách, kéo chuột bôi đen một đoạn theo dòng chữ → overlay xanh mờ bám đúng chữ; thả chuột → nút "Thêm ghi chú" hiện cạnh đoạn.
2. Bấm nút → tab Notes mở, banner "Trích dẫn: …" hiện đúng đoạn; gõ bình luận, Ctrl+Enter → thẻ note có khối trích dẫn (cắt ~3 dòng) + bình luận + badge trang = TRANG CHỨA đoạn chọn.
3. Click thẻ note → nhảy đúng trang đó.
4. Lưu khi để trống bình luận (chỉ trích dẫn) → vẫn tạo note.
5. Esc / cuộn / kéo mới → vùng chọn + nút biến mất.
6. Kiểm tra KHÔNG hỏng: pan/scroll, Ctrl+wheel zoom, double-click sửa text (MicroEditor).
7. Lọc theo từ trong trích dẫn → thẻ hiện đúng.

---

## Self-Review

**1. Spec coverage:**
- Chọn theo dòng chữ (anchor→focus theo CharIndex) → Task 1 resolver + Task 4 mouse. ✅
- Logic chọn thuần test được → Task 1. ✅
- Quote trường riêng + migrate user_version 2 → Task 2. ✅
- Nút nổi "Thêm ghi chú" → Task 4 Step 2-3. ✅
- Mở tab Notes + banner trích dẫn + lưu (cho phép Draft rỗng nếu có quote) + neo trang đoạn chọn → Task 3 + Task 4 Step 5. ✅
- Thẻ note hiện quote cắt ~3 dòng → Task 4 Step 5d. ✅
- Giữ pan/zoom/double-click; chọn trong một trang → Task 4 (TryPageHit cùng trang, ClearSelection trên scroll/Esc) + Step 8 verify. ✅
- Không lưu/vẽ lại highlight bền vững (để 2c) → không có task nào lưu rect. ✅

**2. Placeholder scan:** Task 4 Step 4 ghi rõ "đọc DrawHighlights để khớp cách dịch tọa độ" — đây là yêu cầu khớp pattern hiện có (không phải nội dung thiếu); kèm code mẫu cụ thể. Các task khác có code đầy đủ.

**3. Type consistency:** `Note(Id, OwnerKey, DocumentId, PageIndex, Quote, Content, Created, Updated)` dùng nhất quán (Task 2 đổi, Task 3 dùng); `NoteSelection(Quote, PageIndex)`; `TextSelectionResolver.Resolve/NearestCharIndex` + `SelChar`/`SelectionResult`; `BeginNoteFromSelection(string,int)`; `AddNoteFromSelectionCommand` (VM RelayCommand + control DP); `RightTabIndex`/`PendingQuote`; `PdfObjectManager.GetPageTexts(int)`.
