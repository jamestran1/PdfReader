# Document Library Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Import PDFs into an app-managed library (copied + thumbnailed), then reopen them from a card-grid library view.

**Architecture:** A standalone SQLite-backed `LibraryStore` (separate `library.db`) records library entries; `LibraryService` copies the file into an app folder, renders a cover thumbnail (PDFium + Skia), and upserts the row. `MainViewModel` exposes the library collection + a `ShowLibrary` toggle and reuses the existing document-load path. `MainWindow` shows a card grid when the library is active.

**Tech Stack:** WPF (.NET net10.0-windows), CommunityToolkit.Mvvm, Microsoft.Data.Sqlite, PdfiumViewer (render/page count), SkiaSharp (PNG encode), iText (test PDFs), xUnit.

## Global Constraints

- Target `net10.0-windows`; WPF + MVVM (CommunityToolkit.Mvvm).
- UI strings/comments tiếng Việt, GIỮ DẤU (UTF-8). KHÔNG dùng ký tự em dash.
- KHÔNG thêm `Co-Authored-By` trailer.
- Test: `dotnet test`; build: `dotnet build PdfReaderApp.slnx`.
- Storage: copy to `%APPDATA%/PdfReaderApp/library/<documentId>.pdf`; thumbnail `…/library/thumbs/<documentId>.png`; metadata in `%APPDATA%/PdfReaderApp/library.db`.
- `documentId` = `DocumentId.FromFile(path)` (SHA256 of content) → dedup by id.
- Do NOT touch the AI index (`SqliteDocumentIndex`, `documents` table), search, view modes, or editing.

## File Structure

| File | Trách nhiệm | Hành động |
|---|---|---|
| `src/PdfReaderApp/Models/LibraryItem.cs` | Record 1 mục thư viện | Create |
| `src/PdfReaderApp/Services/ILibraryStore.cs` | Interface store | Create |
| `src/PdfReaderApp/Services/SqliteLibraryStore.cs` | Store SQLite (`library.db`) | Create |
| `src/PdfReaderApp/Services/LibraryService.cs` | Import (copy+thumbnail+upsert), GetAll, Remove, MarkOpened | Create |
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | State `ShowLibrary` + `Library` + commands; refactor OpenFile | Modify |
| `src/PdfReaderApp/MainWindow.xaml` | Lưới thẻ thư viện + nút Read + toggle | Modify |

---

### Task 1: LibraryItem model + ILibraryStore + SqliteLibraryStore

**Files:**
- Create: `src/PdfReaderApp/Models/LibraryItem.cs`, `src/PdfReaderApp/Services/ILibraryStore.cs`, `src/PdfReaderApp/Services/SqliteLibraryStore.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SqliteLibraryStoreTests.cs`

**Interfaces:**
- Produces:
  - `record LibraryItem(string DocumentId, string Title, string StoredPath, string? ThumbPath, int PageCount, long ImportedAtUnix, long LastOpenedAtUnix)`
  - `interface ILibraryStore { void EnsureSchema(); void Upsert(LibraryItem item); IReadOnlyList<LibraryItem> GetAll(); LibraryItem? Get(string documentId); void TouchLastOpened(string documentId, long whenUnix); void Remove(string documentId); }`
  - `class SqliteLibraryStore(string dbPath) : ILibraryStore`

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/SqliteLibraryStoreTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteLibraryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteLibraryStore _store;

    public SqliteLibraryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _store = new SqliteLibraryStore(Path.Combine(_dir, "library.db"));
        _store.EnsureSchema();
    }

    private static LibraryItem Item(string id, long opened) =>
        new(id, id + ".pdf", $"/lib/{id}.pdf", $"/thumb/{id}.png", 10, 100, opened);

    [Fact]
    public void Upsert_Then_Get_ReturnsItem()
    {
        _store.Upsert(Item("aaa", 500));
        var got = _store.Get("aaa");
        Assert.NotNull(got);
        Assert.Equal("aaa.pdf", got!.Title);
        Assert.Equal(10, got.PageCount);
    }

    [Fact]
    public void Upsert_SameId_UpdatesNotDuplicates()
    {
        _store.Upsert(Item("aaa", 500));
        _store.Upsert(Item("aaa", 999));
        Assert.Single(_store.GetAll());
        Assert.Equal(999, _store.Get("aaa")!.LastOpenedAtUnix);
    }

    [Fact]
    public void GetAll_OrderedByLastOpenedDescending()
    {
        _store.Upsert(Item("old", 100));
        _store.Upsert(Item("new", 900));
        _store.Upsert(Item("mid", 500));
        Assert.Equal(new[] { "new", "mid", "old" }, _store.GetAll().Select(i => i.DocumentId));
    }

    [Fact]
    public void TouchLastOpened_UpdatesTimestamp()
    {
        _store.Upsert(Item("aaa", 100));
        _store.TouchLastOpened("aaa", 777);
        Assert.Equal(777, _store.Get("aaa")!.LastOpenedAtUnix);
    }

    [Fact]
    public void Remove_DeletesRow()
    {
        _store.Upsert(Item("aaa", 100));
        _store.Remove("aaa");
        Assert.Null(_store.Get("aaa"));
        Assert.Empty(_store.GetAll());
    }

    [Fact]
    public void Get_Missing_ReturnsNull() => Assert.Null(_store.Get("nope"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SqliteLibraryStoreTests"`
Expected: FAIL compile (`LibraryItem`/`SqliteLibraryStore` not found).

- [ ] **Step 3: Implement the model, interface, and store**

Create `src/PdfReaderApp/Models/LibraryItem.cs`:

```csharp
namespace PdfReaderApp.Models;

/// <summary>Một tài liệu trong thư viện (đã copy vào thư mục app). Thời gian là unix seconds.</summary>
public sealed record LibraryItem(
    string DocumentId,
    string Title,
    string StoredPath,
    string? ThumbPath,
    int PageCount,
    long ImportedAtUnix,
    long LastOpenedAtUnix);
```

Create `src/PdfReaderApp/Services/ILibraryStore.cs`:

```csharp
using System.Collections.Generic;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface ILibraryStore
{
    void EnsureSchema();
    void Upsert(LibraryItem item);
    IReadOnlyList<LibraryItem> GetAll();          // sắp xếp last_opened_at giảm dần
    LibraryItem? Get(string documentId);
    void TouchLastOpened(string documentId, long whenUnix);
    void Remove(string documentId);
}
```

Create `src/PdfReaderApp/Services/SqliteLibraryStore.cs`:

```csharp
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lưu metadata thư viện trong library.db, tách khỏi index.db của AI.</summary>
public sealed class SqliteLibraryStore : ILibraryStore
{
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();

    public SqliteLibraryStore(string dbPath)
    {
        _conn = new SqliteConnection($"Data Source={dbPath};Pooling=False");
        _conn.Open();
    }

    public void EnsureSchema()
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE IF NOT EXISTS library (
  document_id TEXT PRIMARY KEY,
  title TEXT NOT NULL,
  stored_path TEXT NOT NULL,
  thumb_path TEXT,
  page_count INTEGER NOT NULL,
  imported_at INTEGER NOT NULL,
  last_opened_at INTEGER NOT NULL);";
            cmd.ExecuteNonQuery();
        }
    }

    public void Upsert(LibraryItem item)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO library (document_id, title, stored_path, thumb_path, page_count, imported_at, last_opened_at)
VALUES ($id, $title, $path, $thumb, $pc, $imp, $open)
ON CONFLICT(document_id) DO UPDATE SET
  title=$title, stored_path=$path, thumb_path=$thumb, page_count=$pc, last_opened_at=$open;";
            cmd.Parameters.AddWithValue("$id", item.DocumentId);
            cmd.Parameters.AddWithValue("$title", item.Title);
            cmd.Parameters.AddWithValue("$path", item.StoredPath);
            cmd.Parameters.AddWithValue("$thumb", (object?)item.ThumbPath ?? System.DBNull.Value);
            cmd.Parameters.AddWithValue("$pc", item.PageCount);
            cmd.Parameters.AddWithValue("$imp", item.ImportedAtUnix);
            cmd.Parameters.AddWithValue("$open", item.LastOpenedAtUnix);
            cmd.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<LibraryItem> GetAll()
    {
        lock (_lock)
        {
            var list = new List<LibraryItem>();
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, title, stored_path, thumb_path, page_count, imported_at, last_opened_at FROM library ORDER BY last_opened_at DESC";
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(Read(r));
            return list;
        }
    }

    public LibraryItem? Get(string documentId)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "SELECT document_id, title, stored_path, thumb_path, page_count, imported_at, last_opened_at FROM library WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            using var r = cmd.ExecuteReader();
            return r.Read() ? Read(r) : null;
        }
    }

    public void TouchLastOpened(string documentId, long whenUnix)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "UPDATE library SET last_opened_at=$t WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$t", whenUnix);
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    public void Remove(string documentId)
    {
        lock (_lock)
        {
            using var cmd = _conn.CreateCommand();
            cmd.CommandText = "DELETE FROM library WHERE document_id=$id";
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
    }

    private static LibraryItem Read(SqliteDataReader r) => new(
        r.GetString(0), r.GetString(1), r.GetString(2),
        r.IsDBNull(3) ? null : r.GetString(3),
        r.GetInt32(4), r.GetInt64(5), r.GetInt64(6));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SqliteLibraryStoreTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Models/LibraryItem.cs src/PdfReaderApp/Services/ILibraryStore.cs src/PdfReaderApp/Services/SqliteLibraryStore.cs tests/PdfReaderApp.Tests/Services/SqliteLibraryStoreTests.cs
git commit -m "feat: add LibraryItem model and SQLite library store"
```

---

### Task 2: LibraryService (import, thumbnail, dedup)

**Files:**
- Create: `src/PdfReaderApp/Services/LibraryService.cs`
- Test: `tests/PdfReaderApp.Tests/Services/LibraryServiceTests.cs`

**Interfaces:**
- Consumes: `ILibraryStore`, `LibraryItem` (Task 1); `Core.RenderEngine.RenderPage(PdfiumViewer.Core.PdfPage page, float scale, int dpi = 96)`; `DocumentId.FromFile(string)`.
- Produces:
  - `class LibraryService(ILibraryStore store, string libraryDir, string thumbDir, Core.RenderEngine renderEngine)`
  - `LibraryItem Import(string sourcePath, long nowUnix)`
  - `IReadOnlyList<LibraryItem> GetAll()`
  - `void Remove(LibraryItem item)`
  - `void MarkOpened(string documentId, long nowUnix)`

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/LibraryServiceTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using iText.IO.Font;
using iText.Kernel.Font;
using iText.Kernel.Pdf;
using iText.Layout;
using iText.Layout.Element;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class LibraryServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly string _libDir;
    private readonly string _thumbDir;
    private readonly LibraryService _svc;

    public LibraryServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        _libDir = Path.Combine(_dir, "library");
        _thumbDir = Path.Combine(_libDir, "thumbs");
        Directory.CreateDirectory(_dir);
        var store = new SqliteLibraryStore(Path.Combine(_dir, "library.db"));
        store.EnsureSchema();
        _svc = new LibraryService(store, _libDir, _thumbDir, new PdfReaderApp.Core.RenderEngine());
    }

    private string MakePdf(string name)
    {
        string path = Path.Combine(_dir, name);
        using var writer = new PdfWriter(path);
        using var pdf = new PdfDocument(writer);
        using var doc = new Document(pdf);
        var font = PdfFontFactory.CreateFont(@"C:\Windows\Fonts\arial.ttf",
            PdfEncodings.IDENTITY_H, PdfFontFactory.EmbeddingStrategy.PREFER_EMBEDDED);
        doc.Add(new Paragraph("Trang một").SetFont(font));
        doc.Add(new AreaBreak(iText.Layout.Properties.AreaBreakType.NEXT_PAGE));
        doc.Add(new Paragraph("Trang hai").SetFont(font));
        return path;
    }

    [Fact]
    public void Import_CopiesFile_AddsRow_WithTitleAndPageCount()
    {
        var item = _svc.Import(MakePdf("sach.pdf"), nowUnix: 1000);

        Assert.Equal("sach.pdf", item.Title);
        Assert.Equal(2, item.PageCount);
        Assert.True(File.Exists(item.StoredPath), "stored copy should exist");
        Assert.StartsWith(_libDir, item.StoredPath);
        Assert.Single(_svc.GetAll());
    }

    [Fact]
    public void Import_SameContentTwice_DoesNotDuplicate()
    {
        string p = MakePdf("a.pdf");
        var first = _svc.Import(p, 1000);
        var again = _svc.Import(p, 2000);

        Assert.Equal(first.DocumentId, again.DocumentId);
        Assert.Single(_svc.GetAll());
        Assert.Equal(2000, _svc.GetAll()[0].LastOpenedAtUnix); // touched, not re-imported
    }

    [Fact]
    public void Remove_DeletesRowAndStoredFile()
    {
        var item = _svc.Import(MakePdf("b.pdf"), 1000);
        _svc.Remove(item);

        Assert.Empty(_svc.GetAll());
        Assert.False(File.Exists(item.StoredPath), "stored copy should be deleted");
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~LibraryServiceTests"`
Expected: FAIL compile (`LibraryService` not found).

- [ ] **Step 3: Implement LibraryService**

Create `src/PdfReaderApp/Services/LibraryService.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using PdfiumViewer.Core;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using SkiaSharp;

namespace PdfReaderApp.Services;

/// <summary>
/// Quản lý thư viện: import (copy file vào thư mục app + render thumbnail bìa + ghi store),
/// liệt kê, xoá, cập nhật lần mở cuối. Dedup theo DocumentId (hash nội dung).
/// </summary>
public sealed class LibraryService
{
    private const float ThumbTargetWidthPx = 220f;

    private readonly ILibraryStore _store;
    private readonly string _libraryDir;
    private readonly string _thumbDir;
    private readonly RenderEngine _renderEngine;

    public LibraryService(ILibraryStore store, string libraryDir, string thumbDir, RenderEngine renderEngine)
    {
        _store = store;
        _libraryDir = libraryDir;
        _thumbDir = thumbDir;
        _renderEngine = renderEngine;
    }

    public IReadOnlyList<LibraryItem> GetAll() => _store.GetAll();

    public void MarkOpened(string documentId, long nowUnix) => _store.TouchLastOpened(documentId, nowUnix);

    public LibraryItem Import(string sourcePath, long nowUnix)
    {
        string id = DocumentId.FromFile(sourcePath);

        var existing = _store.Get(id);
        if (existing != null)
        {
            _store.TouchLastOpened(id, nowUnix);
            return existing with { LastOpenedAtUnix = nowUnix };
        }

        Directory.CreateDirectory(_libraryDir);
        Directory.CreateDirectory(_thumbDir);

        string storedPath = Path.Combine(_libraryDir, id + ".pdf");
        if (!File.Exists(storedPath))
            File.Copy(sourcePath, storedPath, overwrite: true);

        int pageCount;
        string? thumbPath = Path.Combine(_thumbDir, id + ".png");
        using (var ms = new MemoryStream(File.ReadAllBytes(storedPath)))
        using (var doc = PdfDocument.Load(ms))
        {
            pageCount = doc.PageCount;
            try { RenderThumbnail(doc.Pages[0], thumbPath); }
            catch { thumbPath = null; } // thumbnail là phụ; thiếu vẫn import được
        }

        var item = new LibraryItem(id, Path.GetFileName(sourcePath), storedPath, thumbPath,
            pageCount, nowUnix, nowUnix);
        _store.Upsert(item);
        return item;
    }

    public void Remove(LibraryItem item)
    {
        _store.Remove(item.DocumentId);
        TryDelete(item.StoredPath);
        if (item.ThumbPath != null) TryDelete(item.ThumbPath);
    }

    private void RenderThumbnail(PdfPage page, string thumbPath)
    {
        float scale = ThumbTargetWidthPx / (float)page.Width;
        using var bmp = _renderEngine.RenderPage(page, scale);
        using var img = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 85);
        using var fs = File.Create(thumbPath);
        data.SaveTo(fs);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~LibraryServiceTests"`
Expected: PASS (3 tests). If `RenderPage` (GDI/PDFium) cannot run in the test host, the thumbnail is caught and skipped (thumbPath becomes null); the copy/row/page-count assertions still pass. If a test fails ONLY on a thumbnail file assertion, there is none here by design.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/LibraryService.cs tests/PdfReaderApp.Tests/Services/LibraryServiceTests.cs
git commit -m "feat: add LibraryService (import, thumbnail, dedup, remove)"
```

---

### Task 3: MainViewModel wiring (state, commands, import+open)

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`
- Test: `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: `LibraryService`, `LibraryItem`, `SqliteLibraryStore`, `Core.RenderEngine` (Tasks 1-2).
- Produces: `ShowLibrary` (bool, default true), `Library` (`ObservableCollection<LibraryItem>`), commands `ShowLibraryViewCommand`, `OpenLibraryItemCommand`, `RemoveLibraryItemCommand`; `OpenFile` becomes import+open.

- [ ] **Step 1: Write the failing test**

Add to `tests/PdfReaderApp.Tests/MainViewModelTests.cs`:

```csharp
[Fact]
public void ShowLibraryViewCommand_SetsShowLibraryTrue()
{
    var vm = new MainViewModel();
    vm.ShowLibrary = false;
    vm.ShowLibraryViewCommand.Execute(null);
    Assert.True(vm.ShowLibrary);
}

[Fact]
public void ShowLibrary_DefaultsTrue()
{
    Assert.True(new MainViewModel().ShowLibrary);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests.ShowLibrary"`
Expected: FAIL compile (`ShowLibrary` not defined).

- [ ] **Step 3: Add library state, paths, commands, and refactor OpenFile**

In `src/PdfReaderApp/ViewModels/MainViewModel.cs`:

(a) Add `using System.Collections.Generic;` and `using PdfReaderApp.Models;` if missing (Models is already used). Add a field + paths near the other private fields (after `_ragContext`):

```csharp
    private readonly LibraryService _library;
```

(b) Add a library directory helper next to `IndexDbPath()`:

```csharp
    private static string AppDir()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfReaderApp");
        System.IO.Directory.CreateDirectory(dir);
        return dir;
    }
```

(c) Add observable state near the other `[ObservableProperty]` fields (e.g. after `_showCoverSeparately`):

```csharp
    [ObservableProperty]
    private bool _showLibrary = true;

    public ObservableCollection<LibraryItem> Library { get; } = new();
```

(d) In the constructor body (after `_ragContext = ...;`), construct the library service and load entries:

```csharp
        var libraryStore = new SqliteLibraryStore(System.IO.Path.Combine(AppDir(), "library.db"));
        libraryStore.EnsureSchema();
        _library = new LibraryService(libraryStore,
            System.IO.Path.Combine(AppDir(), "library"),
            System.IO.Path.Combine(AppDir(), "library", "thumbs"),
            new PdfReaderApp.Core.RenderEngine());
        ReloadLibrary();
```

(e) Replace the `OpenFile` command body so it imports then opens; extract the shared load into `LoadActiveDocument`. Replace the existing `OpenFile()` method with:

```csharp
    [RelayCommand]
    private void OpenFile()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "PDF Files (*.pdf)|*.pdf|All Files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        try
        {
            var item = _library.Import(dialog.FileName, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ReloadLibrary();
            OpenLibraryItem(item);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Không thể import file PDF: {ex.Message}", "Lỗi",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void ShowLibraryView()
    {
        ReloadLibrary();
        ShowLibrary = true;
    }

    [RelayCommand]
    private void OpenLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        LoadActiveDocument(item.StoredPath);
        if (_documentId != null)
        {
            _library.MarkOpened(item.DocumentId, DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            ShowLibrary = false;
        }
    }

    [RelayCommand]
    private void RemoveLibraryItem(LibraryItem? item)
    {
        if (item is null) return;
        _library.Remove(item);
        Library.Remove(item);
    }

    private void ReloadLibrary()
    {
        Library.Clear();
        foreach (var i in _library.GetAll()) Library.Add(i);
    }

    // Nạp tài liệu đang hoạt động từ đường dẫn (đã copy trong thư viện). Tái dùng cho cả OpenFile lẫn mở từ thư viện.
    private void LoadActiveDocument(string path)
    {
        FilePath = path;
        try
        {
            _documentService.LoadFile(path);
            _documentBlocks = _analyzer.AnalyzeRich();
            OnPropertyChanged(nameof(DocumentBlocks));
            _pageTexts = _documentService.ExtractPageTexts();

            _documentId = DocumentId.FromFile(path);
            _chatService.ResetConversation();
            SearchResults.Clear();
            StartBackgroundIndexing();
        }
        catch (Exception ex)
        {
            _documentBlocks = new List<TextBlock>();
            OnPropertyChanged(nameof(DocumentBlocks));
            _documentId = null;
            System.Windows.MessageBox.Show($"Không thể mở file PDF: {ex.Message}", "Lỗi mở file",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            FilePath = null;
        }
    }
```

(Delete the old `OpenFile` method body that contained the inline load — its logic now lives in `LoadActiveDocument`. Keep `StartBackgroundIndexing`, `_documentService`, etc. unchanged.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (all existing + 2 new). Build the solution too: `dotnet build PdfReaderApp.slnx` (0 errors).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: library state and commands in MainViewModel; OpenFile imports then opens"
```

---

### Task 4: MainWindow library grid UI

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml`

**Interfaces:**
- Consumes: `ShowLibrary`, `Library`, `ShowLibraryViewCommand`, `OpenLibraryItemCommand`, `RemoveLibraryItemCommand` (Task 3).

No unit tests (XAML/UI). Verify by build + manual.

- [ ] **Step 1: Bind the Read nav-rail button to ShowLibraryViewCommand**

In `src/PdfReaderApp/MainWindow.xaml`, find the rail "Read" button (the `ToggleButton`/`Button` with `ToolTip="Read"` and `PackIcon Kind="BookOpenVariant"`). Add a Command so it opens the library:

```xml
                    <Button Style="{StaticResource MaterialDesignFlatButton}" Foreground="White" Height="60" ToolTip="Thư viện"
                            Command="{Binding ShowLibraryViewCommand}">
                        <materialDesign:PackIcon Kind="BookOpenVariant" Width="24" Height="24"/>
                    </Button>
```

(Replace the existing Read button element; keep its position. If the element is a `<Button ...><materialDesign:PackIcon Kind="BookOpenVariant".../></Button>` without a command, just add the `Command="{Binding ShowLibraryViewCommand}"` and update the ToolTip.)

- [ ] **Step 2: Add the library grid overlay in the content area**

In `src/PdfReaderApp/MainWindow.xaml`, the PDF viewer sits in the center `Grid` at `Grid.Row="1"` (the `<controls:PdfViewerControl Grid.Row="1" x:Name="PdfViewer" .../>`). Add `Visibility` to the viewer and a library grid as a sibling in the same row. Wrap or place right AFTER the `</controls:PdfViewerControl>` (it is self-closing, so add after it), inside the same parent that holds Row 1:

First, add to the `PdfViewerControl` element a visibility bound to the inverse of ShowLibrary. Add this attribute to the control:

```xml
                                       Visibility="{Binding ShowLibrary, Converter={StaticResource InverseBoolToVisibilityConverter}}"
```

Then add the library panel right after the closing `/>` of the PdfViewerControl:

```xml
            <!-- Library grid (shown when ShowLibrary) -->
            <Border Grid.Row="1" Background="{DynamicResource MaterialDesignPaper}"
                    Visibility="{Binding ShowLibrary, Converter={StaticResource BooleanToVisibilityConverter}}">
                <Grid>
                    <TextBlock Text="Chưa có tài liệu — bấm OPEN PDF để thêm vào thư viện."
                               HorizontalAlignment="Center" VerticalAlignment="Center" Opacity="0.6"
                               Visibility="{Binding Library.Count, Converter={StaticResource CountToInverseVisibilityConverter}}"/>
                    <ScrollViewer VerticalScrollBarVisibility="Auto" Padding="16">
                        <ItemsControl ItemsSource="{Binding Library}">
                            <ItemsControl.ItemsPanel>
                                <ItemsPanelTemplate><WrapPanel /></ItemsPanelTemplate>
                            </ItemsControl.ItemsPanel>
                            <ItemsControl.ItemTemplate>
                                <DataTemplate>
                                    <materialDesign:Card Width="180" Margin="8" materialDesign:ElevationAssist.Elevation="Dp2">
                                        <Button Style="{StaticResource MaterialDesignFlatButton}" Padding="0" HorizontalContentAlignment="Stretch"
                                                Command="{Binding DataContext.OpenLibraryItemCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                CommandParameter="{Binding}">
                                            <StackPanel>
                                                <Image Source="{Binding ThumbPath}" Height="220" Stretch="Uniform" Margin="0,8"/>
                                                <TextBlock Text="{Binding Title}" TextTrimming="CharacterEllipsis" Margin="8,0" FontWeight="Bold"/>
                                                <TextBlock Text="{Binding PageCount, StringFormat='{}{0} trang'}" Margin="8,0,8,8" Opacity="0.7" FontSize="11"/>
                                            </StackPanel>
                                        </Button>
                                    </materialDesign:Card>
                                </DataTemplate>
                            </ItemsControl.ItemTemplate>
                        </ItemsControl>
                    </ScrollViewer>
                </Grid>
            </Border>
```

- [ ] **Step 3: Register the converters**

In `src/PdfReaderApp/MainWindow.xaml` `<Window.Resources>`, add:

```xml
        <BooleanToVisibilityConverter x:Key="BooleanToVisibilityConverter" />
        <local:InverseBoolToVisibilityConverter x:Key="InverseBoolToVisibilityConverter" />
        <local:CountToInverseVisibilityConverter x:Key="CountToInverseVisibilityConverter" />
```

In `src/PdfReaderApp/MainWindow.xaml.cs`, add these converter classes (after the existing converters):

```csharp
public sealed class InverseBoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}

public sealed class CountToInverseVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c)
        => value is int n && n > 0 ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => Binding.DoNothing;
}
```

(`using System.Windows;`, `System.Windows.Data;`, `System.Globalization;`, `System;` are already present in that file.)

- [ ] **Step 4: Build and manual-verify**

Run: `dotnet build PdfReaderApp.slnx`
Expected: Build succeeded, 0 errors, no XAML parse errors.

Manual (`dotnet run --project src/PdfReaderApp`):
1. On launch with empty library → library view shows the empty-state text.
2. OPEN PDF → file is imported (copied), library view shows a card with cover thumbnail + title + page count; the document opens (viewer shows, library hides).
3. Click the rail "Thư viện" (Read) button → library grid reappears with the card.
4. Click the card → reopens the document.
5. Re-import the same file → no duplicate card (dedup by content hash).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml src/PdfReaderApp/MainWindow.xaml.cs
git commit -m "feat: library card grid view, Read button opens library"
```

---

## Self-Review

**1. Spec coverage:**
- Copy to app folder + thumbnail → Task 2 (`LibraryService.Import`). ✓
- Separate `library.db` store + schema → Task 1. ✓
- Dedup by content hash → Task 2 (Import checks `Get(id)`). ✓
- Import-then-open via OPEN PDF → Task 3 (`OpenFile`). ✓
- Read button opens library; startup shows library → Task 3 (`ShowLibrary=true` default, `ShowLibraryViewCommand`) + Task 4 (button bind, visibility). ✓
- Card grid w/ thumbnail + title + page count + open + remove → Task 4 (template) + Task 3 (`RemoveLibraryItemCommand`). ✓
- Independent of AI index → separate store/service; no `SqliteDocumentIndex` change. ✓
- Tests: store CRUD/dedup (Task 1), import/dedup/remove (Task 2), VM state (Task 3). ✓

**2. Placeholder scan:** No TBD/TODO. Task 2 Step 4 explains the thumbnail-skip path (not a placeholder). Task 4 Step 1 gives the exact button XAML and notes the alternative of adding only the Command attribute — both are concrete.

**3. Type consistency:** `LibraryItem(DocumentId,Title,StoredPath,ThumbPath,PageCount,ImportedAtUnix,LastOpenedAtUnix)` used identically in Tasks 1-3. `ILibraryStore` methods (Upsert/GetAll/Get/TouchLastOpened/Remove) consistent. `LibraryService(store, libraryDir, thumbDir, RenderEngine)` ctor + `Import(sourcePath, nowUnix)`/`GetAll`/`Remove(item)`/`MarkOpened(id, nowUnix)` consistent across Tasks 2-3. VM members `ShowLibrary`/`Library`/`ShowLibraryViewCommand`/`OpenLibraryItemCommand`/`RemoveLibraryItemCommand` (Task 3) bound in Task 4. `RenderEngine.RenderPage(PdfPage, float, int=96)` matches the existing signature.

**Note for reviewer (RenderEngine in tests):** Task 2's `LibraryService` calls `RenderEngine.RenderPage` which uses PDFium + System.Drawing (GDI). If this cannot execute on the test host, the thumbnail render is caught and `ThumbPath` becomes null; the import still succeeds and Task 2's assertions (copy exists, row added, page count, dedup) hold without asserting a thumbnail file. The plan does not assert thumbnail existence for this reason.
