# Notes Layer 2c — Highlight lưu + vẽ lại trên trang — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Note tạo từ chọn-text để lại highlight vàng được lưu và vẽ lại trên trang mỗi khi hiển thị; xóa note thì mất highlight.

**Architecture:** `Note` thêm anchor `Rects`+`Color` (migrate user_version 3). Luồng chọn mang rects vào note khi Save. `NotesViewModel.Highlights` (note có rects) đẩy qua DependencyProperty tới `PdfViewerControl`, vẽ bằng `DrawSavedHighlights` (top-down, không lật Y, đúng hệ overlay 2b).

**Tech Stack:** WPF .NET 10, CommunityToolkit.Mvvm, SkiaSharp, Microsoft.Data.Sqlite, System.Text.Json, xUnit.

## Global Constraints

- Comment/chuỗi UI tiếng Việt GIỮ DẤU. Không dùng dấu gạch ngang dài (em dash).
- Tọa độ rect: PDF **top-origin** (đúng hệ `_selectionRectsPdf`/`DrawSelectionOverlay` đã fix). Vẽ: `pageRect.Top + Y*scale`, KHÔNG lật Y.
- Màu mặc định `#FFEB3B`; vẽ mờ (alpha ~80). Lưu cột màu để sau mở palette.
- Chỉ note có `Rects` (từ chọn-text) mới vẽ; note AI/tự-do `Rects=null`.
- `Highlights` hiển thị BẤT KỂ FilterText (highlight luôn trên trang).
- Migrate SQLite: `ALTER TABLE ... ADD COLUMN` chỉ khi thiếu (qua `PRAGMA table_info`), set `user_version = 3`.
- v1 KHÔNG click-trên-trang (issue #30).

---

### Task 1: HighlightRect + Note.Rects/Color + migrate store v3

**Files:**
- Create: `src/PdfReaderApp/Models/HighlightRect.cs`
- Modify: `src/PdfReaderApp/Models/Note.cs`
- Modify: `src/PdfReaderApp/Services/SqliteNoteStore.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs`

**Interfaces:**
- Produces:
  - `record HighlightRect(double X, double Y, double W, double H)` (PdfReaderApp.Models).
  - `Note` thêm 2 tham số positional CÓ DEFAULT ở CUỐI: `IReadOnlyList<HighlightRect>? Rects = null, string? Color = null` (nên các `new Note(...)` 8-tham-số hiện có vẫn biên dịch).
  - `SqliteNoteStore` schema v3: cột `rects TEXT` (JSON) + `color TEXT`; Add ghi, GetForOwner đọc.

- [ ] **Step 1: Viết test thất bại**

Thêm vào `tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs`:

```csharp
    [Fact]
    public void Add_WithRectsAndColor_RoundTrips()
    {
        var rects = new List<HighlightRect> { new(1, 2, 30, 10), new(1, 14, 25, 10) };
        _store.Add(new Note("h1", "docA", "docA", 3, "trích", "ghi chú", 10, 10, rects, "#FFEB3B"));
        var got = _store.GetForOwner("docA").Single();
        Assert.NotNull(got.Rects);
        Assert.Equal(2, got.Rects!.Count);
        Assert.Equal(30, got.Rects[0].W);
        Assert.Equal("#FFEB3B", got.Color);
    }

    [Fact]
    public void Add_NullRects_RoundTripsAsNull()
    {
        _store.Add(new Note("n1", "docA", "docA", 1, null, "tự do", 1, 1));
        var got = _store.GetForOwner("docA").Single();
        Assert.Null(got.Rects);
        Assert.Null(got.Color);
    }

    [Fact]
    public void EnsureSchema_MigratesV2DbByAddingRectsAndColor()
    {
        string db = Path.Combine(_dir, "v2.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var c = conn.CreateCommand();
            // bảng kiểu v2 (có quote, CHƯA có rects/color), user_version=2, 1 dòng
            c.CommandText = @"CREATE TABLE note (id TEXT PRIMARY KEY, owner_key TEXT NOT NULL, document_id TEXT,
                page_index INTEGER, quote TEXT, content TEXT NOT NULL, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
                PRAGMA user_version = 2;
                INSERT INTO note (id, owner_key, document_id, page_index, quote, content, created_at, updated_at)
                VALUES ('old','docA','docA',1,'q','cũ',1,1);";
            c.ExecuteNonQuery();
        }

        var store = new SqliteNoteStore(db);
        store.EnsureSchema();

        var got = store.GetForOwner("docA").Single();
        Assert.Equal("cũ", got.Content);
        Assert.Null(got.Rects);
        store.Add(new Note("new", "docA", "docA", 1, "q2", "mới", 2, 2,
            new List<HighlightRect> { new(0, 0, 5, 5) }, "#FFEB3B"));
        Assert.Contains(store.GetForOwner("docA"), n => n.Rects != null && n.Rects.Count == 1);
    }
```

(Thêm `using PdfReaderApp.Models;` nếu thiếu — file đã dùng `Note` nên thường đã có.)

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~SqliteNoteStoreTests.Add_WithRectsAndColor"`
Expected: FAIL biên dịch (`HighlightRect`/`Note.Rects` chưa có).

- [ ] **Step 3: Tạo HighlightRect + sửa Note**

`src/PdfReaderApp/Models/HighlightRect.cs`:

```csharp
namespace PdfReaderApp.Models;

/// <summary>Một dải highlight trên trang, tọa độ PDF top-origin (Y hướng xuống).</summary>
public sealed record HighlightRect(double X, double Y, double W, double H);
```

`src/PdfReaderApp/Models/Note.cs` — thêm 2 tham số CÓ DEFAULT ở cuối:

```csharp
using System.Collections.Generic;

namespace PdfReaderApp.Models;

/// <summary>Một ghi chú. OwnerKey = phạm vi (v1 = documentId), DocumentId = anchor.
/// Quote = đoạn trích dẫn; Rects/Color = highlight trên trang (null nếu note không từ chọn-text).</summary>
public sealed record Note(
    string Id,
    string OwnerKey,
    string? DocumentId,
    int? PageIndex,
    string? Quote,
    string Content,
    long CreatedAtUnixMs,
    long UpdatedAtUnixMs,
    IReadOnlyList<HighlightRect>? Rects = null,
    string? Color = null);
```

- [ ] **Step 4: Sửa SqliteNoteStore (v3 + rects/color)**

Trong `src/PdfReaderApp/Services/SqliteNoteStore.cs`:

4a. `private const long SchemaVersion = 3;`

4b. Trong `EnsureSchema`: CREATE TABLE thêm `rects TEXT,` và `color TEXT,` (đặt sau `quote TEXT,`). Sau khối CREATE + index, thêm migrate cho db cũ (dùng `ColumnExists` đã có từ migrate quote):

```csharp
            if (!ColumnExists(conn, "note", "rects"))
            {
                using var a1 = conn.CreateCommand();
                a1.CommandText = "ALTER TABLE note ADD COLUMN rects TEXT;";
                a1.ExecuteNonQuery();
            }
            if (!ColumnExists(conn, "note", "color"))
            {
                using var a2 = conn.CreateCommand();
                a2.CommandText = "ALTER TABLE note ADD COLUMN color TEXT;";
                a2.ExecuteNonQuery();
            }
```

(Giữ khối `PRAGMA user_version` hiện có — `SchemaVersion=3` nên set lên 3.)

4c. `Add`: thêm cột rects/color vào INSERT. Serialize rects:

```csharp
            cmd.CommandText = @"
INSERT INTO note (id, owner_key, document_id, page_index, quote, content, created_at, updated_at, rects, color)
VALUES ($id, $owner, $doc, $page, $quote, $content, $created, $updated, $rects, $color);";
            // ... các tham số cũ giữ nguyên, thêm:
            string? rectsJson = note.Rects == null ? null
                : System.Text.Json.JsonSerializer.Serialize(note.Rects);
            cmd.Parameters.AddWithValue("$rects", (object?)rectsJson ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$color", (object?)note.Color ?? System.DBNull.Value);
```

4d. `GetForOwner`: SELECT thêm `rects, color` (cuối), đọc + deserialize:

```csharp
            cmd.CommandText = "SELECT id, owner_key, document_id, page_index, quote, content, created_at, updated_at, rects, color FROM note WHERE owner_key=$owner";
            ...
            while (r.Read())
            {
                IReadOnlyList<HighlightRect>? rects = null;
                if (!r.IsDBNull(8))
                {
                    try { rects = System.Text.Json.JsonSerializer.Deserialize<List<HighlightRect>>(r.GetString(8)); }
                    catch { rects = null; }
                }
                string? color = r.IsDBNull(9) ? null : r.GetString(9);
                list.Add(new Note(
                    r.GetString(0), r.GetString(1),
                    r.IsDBNull(2) ? null : r.GetString(2),
                    r.IsDBNull(3) ? (int?)null : r.GetInt32(3),
                    r.IsDBNull(4) ? null : r.GetString(4),
                    r.GetString(5), r.GetInt64(6), r.GetInt64(7),
                    rects, color));
            }
```

(`Update`/`Delete` giữ nguyên.)

- [ ] **Step 5: Chạy test + toàn bộ**

Run: `dotnet test --filter "FullyQualifiedName~SqliteNoteStoreTests"`
Expected: PASS (gồm 3 test mới).

Run: `dotnet build PdfReaderApp.slnx` → 0 Errors. `dotnet test` → toàn bộ xanh (các `new Note(...)` cũ vẫn biên dịch nhờ default).

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Models/HighlightRect.cs src/PdfReaderApp/Models/Note.cs src/PdfReaderApp/Services/SqliteNoteStore.cs tests/PdfReaderApp.Tests/Services/SqliteNoteStoreTests.cs
git commit -m "feat: Note.Rects/Color + SQLite migration to user_version 3"
```

---

### Task 2: NotesViewModel rects + Highlights + selection flow

**Files:**
- Modify: `src/PdfReaderApp/Models/NoteSelection.cs`
- Modify: `src/PdfReaderApp/ViewModels/NotesViewModel.cs`
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`

**Interfaces:**
- Consumes: `Note` (có Rects/Color), `HighlightRect` (Task 1).
- Produces:
  - `NoteSelection(string Quote, int PageIndex, IReadOnlyList<HighlightRect> Rects)`.
  - `NotesViewModel.BeginNoteFromSelection(string quote, int pageIndex, IReadOnlyList<HighlightRect> rects)`; `ObservableCollection<Note> Highlights`.
  - `MainViewModel.AddNoteFromSelection` truyền `sel.Rects`.

- [ ] **Step 1: Viết test thất bại**

Thêm vào `tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs`:

```csharp
    private static List<HighlightRect> SampleRects() => new() { new(1, 2, 30, 10) };

    [Fact]
    public void Save_FromSelection_AttachesRectsAndYellowColor_AndAddsToHighlights()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 4);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("đoạn trích", 4, SampleRects());
        vm.Draft = "bình luận";

        vm.SaveCommand.Execute(null);

        var saved = store.Rows.Single();
        Assert.NotNull(saved.Rects);
        Assert.Equal("#FFEB3B", saved.Color);
        Assert.Equal(4, saved.PageIndex);
        Assert.Contains(vm.Highlights, n => n.Id == saved.Id);
    }

    [Fact]
    public void AddNote_NoRects_NotInHighlights()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 1);
        vm.LoadFor("doc1");
        vm.AddNote("câu trả lời AI", null, null); // 2a: không rects
        Assert.Empty(vm.Highlights);
    }

    [Fact]
    public void Delete_RemovesFromHighlights()
    {
        var store = new FakeNoteStore();
        var vm = Make(store, page: 2);
        vm.LoadFor("doc1");
        vm.BeginNoteFromSelection("q", 2, SampleRects());
        vm.SaveCommand.Execute(null);
        var note = vm.Highlights.Single();

        vm.DeleteCommand.Execute(note);

        Assert.Empty(vm.Highlights);
    }

    [Fact]
    public void LoadFor_RebuildsHighlights_OnlyRectBearingNotes_IgnoringFilter()
    {
        var store = new FakeNoteStore();
        store.Add(new Note("a", "doc1", "doc1", 1, "q", "có rects", 1, 1, SampleRects(), "#FFEB3B"));
        store.Add(new Note("b", "doc1", "doc1", 1, null, "không rects", 2, 2));
        var vm = Make(store, page: 1);

        vm.FilterText = "zzz"; // không khớp gì
        vm.LoadFor("doc1");

        Assert.Single(vm.Highlights);             // chỉ note có rects
        Assert.Equal("a", vm.Highlights[0].Id);
        Assert.Empty(vm.Items);                   // filter vẫn ẩn khỏi danh sách
    }
```

- [ ] **Step 2: Chạy test, xác nhận thất bại**

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests.Save_FromSelection|FullyQualifiedName~NotesViewModelTests.AddNote_NoRects|FullyQualifiedName~NotesViewModelTests.Delete_RemovesFromHighlights|FullyQualifiedName~NotesViewModelTests.LoadFor_RebuildsHighlights"`
Expected: FAIL biên dịch.

- [ ] **Step 3: Mở rộng NoteSelection**

`src/PdfReaderApp/Models/NoteSelection.cs`:

```csharp
using System.Collections.Generic;

namespace PdfReaderApp.Models;

/// <summary>Vùng chọn text để tạo note: đoạn trích, trang (0-based), và các rect highlight (top-origin).</summary>
public sealed record NoteSelection(string Quote, int PageIndex, IReadOnlyList<HighlightRect> Rects);
```

- [ ] **Step 4: NotesViewModel — pending rects + Highlights**

Trong `src/PdfReaderApp/ViewModels/NotesViewModel.cs`:

4a. Thêm hằng + field + collection (cạnh `_pendingPageIndex`):

```csharp
    private const string DefaultHighlightColor = "#FFEB3B";
    private IReadOnlyList<HighlightRect>? _pendingRects;
    public ObservableCollection<Note> Highlights { get; } = new();
```

4b. Sửa `BeginNoteFromSelection` để nhận rects:

```csharp
    public void BeginNoteFromSelection(string quote, int pageIndex, IReadOnlyList<HighlightRect> rects)
    {
        CancelEdit();
        PendingQuote = quote;
        _pendingPageIndex = pageIndex;
        _pendingRects = rects;
        RightTabIndex = 1;
    }
```

4c. Trong `Save` (nhánh tạo mới `_editingId == null`): gắn rects/color khi có pending rects, và thêm vào Highlights. Sửa khối tạo note:

```csharp
            int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();
            var rects = _pendingRects;
            var color = rects != null ? DefaultHighlightColor : null;
            var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey,
                page, PendingQuote, content, now, now, rects, color);
            try { _store.Add(note); }
            catch { return; }
            _all.Add(note);
            if (MatchesFilter(note, FilterText)) InsertSorted(note);
            if (note.Rects != null && note.Rects.Count > 0) Highlights.Add(note);
```

Và `_pendingRects = null;` được xóa cùng pending — thêm vào cuối `Save` (sau `CancelEdit()`), HOẶC trong `CancelEdit`. Đặt vào `CancelEdit` cho gọn (xem 4e).

4d. Trong `AddNote` (2a, one-click): KHÔNG đụng rects (note AI không highlight) — giữ nguyên; nó tạo `new Note(..., now, now)` (Rects/Color mặc định null) nên không vào Highlights. (Không cần sửa.)

4e. `CancelEdit`: thêm `_pendingRects = null;` (cùng `PendingQuote = null;`).

4f. `Delete(Note note)`: sau khi xóa khỏi `_all`/`Items`, thêm gỡ khỏi Highlights:

```csharp
        int hi = -1;
        for (int i = 0; i < Highlights.Count; i++) if (Highlights[i].Id == note.Id) { hi = i; break; }
        if (hi >= 0) Highlights.RemoveAt(hi);
```

4g. `LoadFor`: sau khi nạp `_all` + `RebuildItems()`, rebuild Highlights (bỏ qua filter):

```csharp
        Highlights.Clear();
        foreach (var n in _all)
            if (n.Rects != null && n.Rects.Count > 0) Highlights.Add(n);
```

- [ ] **Step 5: MainViewModel truyền rects**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`, sửa `AddNoteFromSelection`:

```csharp
    [RelayCommand]
    private void AddNoteFromSelection(PdfReaderApp.Models.NoteSelection? sel)
    {
        if (sel is null) return;
        Notes.BeginNoteFromSelection(sel.Quote, sel.PageIndex, sel.Rects);
    }
```

- [ ] **Step 6: Build + test**

Run: `dotnet build PdfReaderApp.slnx` → 0 Errors (lưu ý: `PdfViewerControl` dựng `NoteSelection` 2-tham-số sẽ LỖI biên dịch giờ cần 3 — sẽ sửa ở Task 3; nếu muốn build xanh giữa chừng, Task 3 phải xong. Để TDD theo task, chạy test lọc riêng NotesViewModel trước:)

Run: `dotnet test --filter "FullyQualifiedName~NotesViewModelTests"`
Expected: PASS (gồm 4 test mới). (Build toàn solution có thể đỏ vì PdfViewerControl — Task 3 sửa nốt; test project tham chiếu src nên cũng cần Task 3 để `dotnet test` toàn bộ xanh. Vì vậy COMMIT task 2 sau khi test NotesViewModel xanh, build app có thể tạm đỏ — Task 3 đóng lại.)

> Ghi chú thực thi: vì `NoteSelection` đổi chữ ký phá `PdfViewerControl` (Task 3), Task 2 và Task 3 nên được review như một cặp; controller chạy `dotnet test` đầy đủ SAU Task 3. Nếu reviewer cần build xanh để review Task 2, gộp review Task 2+3.

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/Models/NoteSelection.cs src/PdfReaderApp/ViewModels/NotesViewModel.cs src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/ViewModels/NotesViewModelTests.cs
git commit -m "feat: NotesViewModel highlight rects flow + Highlights collection"
```

---

### Task 3: PdfViewerControl vẽ lại highlight + XAML wiring

**Files:**
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs`
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `NoteSelection` (3-arg), `HighlightRect`, `Note.Rects/Color` (Task 1/2); `Notes.Highlights` (Task 2).
- Produces: `PdfViewerControl.Highlights` (DependencyProperty, `IEnumerable`); `DrawSavedHighlights`.

**Bối cảnh (đọc trước khi sửa):**
- `AddNoteButton_Click` (~355) dựng `new Models.NoteSelection(_selectionText, _selPageIndex)` — cần thêm rects.
- `_selectionRectsPdf` là `List<System.Windows.Rect>` (top-origin PDF coords).
- `OnPaintCanvas` (~591) lặp `_slots`, mỗi slot gọi `DrawHighlights(canvas, slot.PageIndex, rect, scale)` + `DrawSelectionOverlay(...)` (~634-635). `DrawSelectionOverlay` (~640) là mẫu vẽ top-down.
- SKElement overlay được vẽ trong `OnPaintCanvas`; tên element lấy từ `sender`/XAML — dùng `InvalidateVisual()` của nó để repaint.

- [ ] **Step 1: NoteSelection mang rects trong AddNoteButton_Click**

Sửa chỗ dựng NoteSelection (~359): chuyển `_selectionRectsPdf` (System.Windows.Rect) → `HighlightRect`:

```csharp
            var rects = _selectionRectsPdf
                .Select(r => new Models.HighlightRect(r.Left, r.Top, r.Width, r.Height))
                .ToList();
            var sel = new Models.NoteSelection(_selectionText, _selPageIndex, rects);
```

(Bảo đảm `using System.Linq;` có ở đầu file.)

- [ ] **Step 2: DependencyProperty Highlights + repaint**

Thêm (cạnh DP `AddNoteFromSelectionCommand`):

```csharp
    public static readonly DependencyProperty HighlightsProperty =
        DependencyProperty.Register(nameof(Highlights), typeof(System.Collections.IEnumerable),
            typeof(PdfViewerControl), new PropertyMetadata(null, OnHighlightsChanged));

    public System.Collections.IEnumerable? Highlights
    {
        get => (System.Collections.IEnumerable?)GetValue(HighlightsProperty);
        set => SetValue(HighlightsProperty, value);
    }

    private static void OnHighlightsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var c = (PdfViewerControl)d;
        if (e.OldValue is System.Collections.Specialized.INotifyCollectionChanged oldCol)
            oldCol.CollectionChanged -= c.OnHighlightsCollectionChanged;
        if (e.NewValue is System.Collections.Specialized.INotifyCollectionChanged newCol)
            newCol.CollectionChanged += c.OnHighlightsCollectionChanged;
        c.RepaintOverlay();
    }

    private void OnHighlightsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        => RepaintOverlay();
```

`RepaintOverlay()`: gọi `InvalidateVisual()` của SKElement overlay (đọc tên element trong `OnPaintCanvas`/XAML, ví dụ `OverlayCanvas.InvalidateVisual()`). Nếu đã có hàm refresh overlay sẵn thì dùng lại.

- [ ] **Step 3: DrawSavedHighlights + gọi trong OnPaintCanvas**

Thêm method (mẫu theo `DrawSelectionOverlay`, top-down):

```csharp
    private void DrawSavedHighlights(SKCanvas canvas, int pageIndex, System.Windows.Rect pageRect, float scale)
    {
        if (Highlights == null) return;
        foreach (var obj in Highlights)
        {
            if (obj is not PdfReaderApp.Models.Note note) continue;
            if (note.PageIndex != pageIndex || note.Rects == null) continue;
            var color = ParseHighlightColor(note.Color);
            using var paint = new SKPaint { Color = color, Style = SKPaintStyle.Fill };
            foreach (var r in note.Rects)
            {
                canvas.DrawRect(SKRect.Create(
                    (float)(pageRect.Left + r.X * scale),
                    (float)(pageRect.Top + r.Y * scale),
                    (float)(r.W * scale),
                    (float)(r.H * scale)), paint);
            }
        }
    }

    private static SKColor ParseHighlightColor(string? hex)
    {
        // Mặc định vàng mờ; parse #RRGGBB nếu có.
        byte rr = 255, gg = 235, bb = 59;
        if (!string.IsNullOrEmpty(hex) && hex.StartsWith("#") && hex.Length == 7
            && byte.TryParse(hex.Substring(1, 2), System.Globalization.NumberStyles.HexNumber, null, out var pr)
            && byte.TryParse(hex.Substring(3, 2), System.Globalization.NumberStyles.HexNumber, null, out var pg)
            && byte.TryParse(hex.Substring(5, 2), System.Globalization.NumberStyles.HexNumber, null, out var pb))
        { rr = pr; gg = pg; bb = pb; }
        return new SKColor(rr, gg, bb, 80);
    }
```

Trong `OnPaintCanvas`, ngay sau `DrawSelectionOverlay(canvas, slot.PageIndex, rect, scale);` (~635) thêm:

```csharp
                DrawSavedHighlights(canvas, slot.PageIndex, rect, scale);
```

- [ ] **Step 4: Bind Highlights trong MainWindow.xaml**

Trên phần tử `<controls:PdfViewerControl x:Name="PdfViewer" ... />`, thêm thuộc tính:

```xml
    Highlights="{Binding Notes.Highlights}"
```

- [ ] **Step 5: Build + toàn bộ test**

Run: `dotnet build PdfReaderApp.slnx` → 0 Errors.
Run: `dotnet test` → toàn bộ xanh (gồm test Task 1 + Task 2).

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: draw saved highlights on page + Highlights DependencyProperty"
```

- [ ] **Step 7: Manual GUI verify (người dùng chạy app)**

1. Chọn text → "Thêm ghi chú" → lưu: highlight vàng hiện đúng đoạn trên trang.
2. Cuộn đi/lại, zoom in/out, đóng-mở sách: highlight vẫn đúng vị trí.
3. Xóa note ở tab Notes → highlight biến mất ngay.
4. Note AI (2a) không tạo highlight.
5. Không hỏng: overlay vùng-chọn tạm (2b) khi đang kéo vẫn hiện; search highlight đúng; pan/zoom/double-click ổn.

---

## Self-Review

**1. Spec coverage:**
- Note.Rects/Color + migrate v3 → Task 1. ✅
- Luồng chọn → rects → note (NoteSelection.Rects, _pendingRects, Save gắn rects+vàng) → Task 2 + Task 3 Step 1. ✅
- Highlights collection (bỏ qua filter; add/delete/loadfor) → Task 2. ✅
- Vẽ lại top-down + repaint khi cuộn/zoom/đổi tập → Task 3 (DrawSavedHighlights + DP + CollectionChanged). ✅
- Xóa note → mất highlight; note AI không highlight → Task 2 (Delete/AddNote) + test. ✅
- Màu vàng mặc định, parse hex → Task 3 ParseHighlightColor. ✅
- v1 không click highlight → không có task nào thêm hit-test. ✅

**2. Placeholder scan:** Không TBD/TODO. Task 3 Step 2 "đọc tên SKElement overlay" là yêu cầu khớp tên thật trong file (kèm ví dụ), không phải nội dung thiếu.

**3. Type consistency:** `HighlightRect(X,Y,W,H)`; `Note(..., IReadOnlyList<HighlightRect>? Rects=null, string? Color=null)`; `NoteSelection(Quote, PageIndex, Rects)`; `BeginNoteFromSelection(string,int,IReadOnlyList<HighlightRect>)`; `Highlights` (ObservableCollection<Note> ở VM, DependencyProperty IEnumerable ở control); `DefaultHighlightColor="#FFEB3B"` — nhất quán Task 1/2/3 và test.

**Lưu ý thực thi (cross-task build):** đổi chữ ký `NoteSelection` (Task 2) phá `PdfViewerControl` cho tới khi Task 3 sửa. `dotnet test` toàn bộ chỉ xanh sau Task 3; Task 2 verify bằng `--filter NotesViewModelTests`. Controller nên review Task 2 + Task 3 như một cặp (build/full-suite sau Task 3).
