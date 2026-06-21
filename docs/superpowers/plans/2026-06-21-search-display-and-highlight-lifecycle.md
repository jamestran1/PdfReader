# Search Display & Highlight Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Làm kết quả search hiển thị to/rõ/có dấu với từ khóa in đậm, và cho highlight trên trang tự biến mất khi người dùng ngừng search.

**Architecture:** Giữ nguyên kiến trúc Hybrid Engine + index SQLite/FTS5 + render SkiaSharp. Thêm 1 helper fold-kèm-bản-đồ-vị-trí để dựng snippet có dấu từ text gốc, 1 component UI in đậm từ khóa, và 1 quy tắc clear highlight trong ViewModel. Không thay đổi `SearchResult` shape, không thêm tọa độ/bounding box.

**Tech Stack:** WPF (.NET net10.0-windows), MVVM với CommunityToolkit.Mvvm (`[ObservableProperty]`, `[RelayCommand]`), MaterialDesignThemes, SQLite FTS5 (trigram), xUnit.

## Global Constraints

- Target framework: `net10.0-windows`; WPF + MVVM (CommunityToolkit.Mvvm).
- Chuỗi UI bằng tiếng Việt.
- KHÔNG dùng ký tự em dash trong code/comment/commit; dùng "..." (ba dấu chấm ASCII) cho ellipsis.
- KHÔNG thêm trailer `Co-Authored-By` vào commit.
- Test: xUnit trong `tests/PdfReaderApp.Tests`. Build: `dotnet build PdfReaderApp.slnx`. Chạy test: `dotnet test`.
- `SearchResult` giữ shape `(int PageIndex, string Snippet, long ChunkId)` (record trong `src/PdfReaderApp/Models/Chunk.cs`); chỉ đổi *ngữ nghĩa* `Snippet` thành text gốc có dấu.

## File Structure

| File | Trách nhiệm | Hành động |
|---|---|---|
| `src/PdfReaderApp/Services/SearchNormalizer.cs` | Fold accent-insensitive + helper fold-kèm-bản-đồ-vị-trí | Modify (thêm `FoldWithMap`) |
| `src/PdfReaderApp/Services/SearchSnippetBuilder.cs` | Dựng đoạn snippet có dấu (text gốc) quanh match | Create |
| `src/PdfReaderApp/Services/SqliteDocumentIndex.cs` | Truy vấn FTS, trả `SearchResult` với snippet có dấu | Modify (`SearchText`) |
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | State search + vòng đời highlight + lệnh clear | Modify |
| `src/PdfReaderApp/SearchSnippetHighlighter.cs` | Tách snippet thành đoạn thường/đậm + attached property cho TextBlock | Create |
| `src/PdfReaderApp/MainWindow.xaml` | Popup to hơn, nút X, bind highlighter | Modify |

Tests song song trong `tests/PdfReaderApp.Tests/Services/` và `tests/PdfReaderApp.Tests/`.

---

### Task 1: `SearchNormalizer.FoldWithMap`

**Files:**
- Modify: `src/PdfReaderApp/Services/SearchNormalizer.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SearchNormalizerTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `public static (string folded, int[] map) FoldWithMap(string s)` — `folded` bằng đúng `Fold(s)`; `map.Length == folded.Length`; `map[i]` = chỉ số ký tự trong chuỗi gốc `s` tương ứng ký tự folded thứ `i`.

- [ ] **Step 1: Write the failing tests**

Thêm vào cuối class `SearchNormalizerTests` trong `tests/PdfReaderApp.Tests/Services/SearchNormalizerTests.cs`:

```csharp
[Fact]
public void FoldWithMap_FoldedMatchesFold()
{
    string[] inputs = { "Tiếng Việt", "Đường", "kinh  hành", "  Tiếng   Việt ", "bảo hiểm" };
    foreach (var s in inputs)
    {
        var (folded, _) = SearchNormalizer.FoldWithMap(s);
        Assert.Equal(SearchNormalizer.Fold(s), folded);
    }
}

[Fact]
public void FoldWithMap_MapLengthEqualsFoldedLength()
{
    var (folded, map) = SearchNormalizer.FoldWithMap("  Tiếng   Việt ");
    Assert.Equal(folded.Length, map.Length);
}

[Fact]
public void FoldWithMap_MapsMatchBackToOriginal()
{
    // "bảo hiểm" -> folded "bao hiem"; folded index 4 ('h') maps to original index 4 ('h')
    string s = "bảo hiểm";
    var (folded, map) = SearchNormalizer.FoldWithMap(s);
    int idx = folded.IndexOf("hiem", StringComparison.Ordinal);
    Assert.True(idx >= 0);
    Assert.Equal('h', s[map[idx]]);
    Assert.Equal('ể', s[map[idx + 2]]); // folded 'e' (3rd char of "hiem") maps to 'ể'
}

[Fact]
public void FoldWithMap_Empty_ReturnsEmpty()
{
    var (folded, map) = SearchNormalizer.FoldWithMap("");
    Assert.Equal("", folded);
    Assert.Empty(map);
}
```

(File đã có `using PdfReaderApp.Services;` và `using System.Text;`. Thêm `using System;` nếu chưa có — cần cho `StringComparison`.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SearchNormalizerTests.FoldWithMap"`
Expected: FAIL với lỗi compile "does not contain a definition for 'FoldWithMap'".

- [ ] **Step 3: Implement `FoldWithMap`**

Thêm `using System.Collections.Generic;` vào đầu `src/PdfReaderApp/Services/SearchNormalizer.cs`, rồi thêm method này vào class `SearchNormalizer` (ngay sau `Fold`):

```csharp
/// <summary>
/// Như Fold nhưng trả thêm bản đồ vị trí: map[i] là chỉ số ký tự trong chuỗi gốc
/// tương ứng ký tự folded thứ i. Dùng để định vị match trên text gốc (giữ dấu).
/// </summary>
public static (string folded, int[] map) FoldWithMap(string s)
{
    if (string.IsNullOrEmpty(s)) return ("", System.Array.Empty<int>());

    // Phase 1: fold theo từng ký tự gốc, ghi nhớ chỉ số nguồn.
    var chars = new List<char>(s.Length);
    var src = new List<int>(s.Length);
    for (int i = 0; i < s.Length; i++)
    {
        char c0 = s[i];
        if (c0 == 'đ') c0 = 'd';
        else if (c0 == 'Đ') c0 = 'D';

        string decomposed = c0.ToString().Normalize(NormalizationForm.FormD);
        foreach (char d in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(d) == UnicodeCategory.NonSpacingMark)
                continue;
            chars.Add(char.ToLowerInvariant(d));
            src.Add(i);
        }
    }

    // Phase 2: gộp run whitespace thành 1 space, trim hai đầu (khớp _whitespace.Replace + Trim).
    var sb = new StringBuilder(chars.Count);
    var map = new List<int>(chars.Count);
    bool pendingSpace = false;
    int pendingSrc = 0;
    for (int k = 0; k < chars.Count; k++)
    {
        if (char.IsWhiteSpace(chars[k]))
        {
            if (!pendingSpace) { pendingSpace = true; pendingSrc = src[k]; }
            continue;
        }
        if (pendingSpace && sb.Length > 0) // run whitespace nội bộ -> 1 space
        {
            sb.Append(' ');
            map.Add(pendingSrc);
        }
        pendingSpace = false;
        sb.Append(chars[k]);
        map.Add(src[k]);
    }
    // pendingSpace còn lại ở cuối bị bỏ (trim trailing).

    return (sb.ToString(), map.ToArray());
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SearchNormalizerTests.FoldWithMap"`
Expected: PASS (4 tests). Nếu `FoldWithMap_FoldedMatchesFold` fail, kiểm tra khác biệt whitespace giữa `char.IsWhiteSpace` và regex `\s` rồi điều chỉnh Phase 2.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/SearchNormalizer.cs tests/PdfReaderApp.Tests/Services/SearchNormalizerTests.cs
git commit -m "feat: add SearchNormalizer.FoldWithMap for accent-aware offset mapping"
```

---

### Task 2: `SearchSnippetBuilder.Build`

**Files:**
- Create: `src/PdfReaderApp/Services/SearchSnippetBuilder.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SearchSnippetBuilderTests.cs`

**Interfaces:**
- Consumes: `SearchNormalizer.Fold(string)`, `SearchNormalizer.FoldWithMap(string)` (Task 1).
- Produces: `public static string Build(string originalText, string query, int contextChars = 40)` — trả đoạn trích text gốc (có dấu) quanh match, thêm "..." khi bị cắt; nếu không tìm thấy match thì trả phần đầu text gốc.

- [ ] **Step 1: Write the failing tests**

Tạo `tests/PdfReaderApp.Tests/Services/SearchSnippetBuilderTests.cs`:

```csharp
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SearchSnippetBuilderTests
{
    [Fact]
    public void Build_PreservesDiacritics()
    {
        string text = "Hợp đồng bảo hiểm có hiệu lực từ ngày ký kết";
        string snip = SearchSnippetBuilder.Build(text, "bao hiem");
        Assert.Contains("bảo hiểm", snip);
    }

    [Fact]
    public void Build_AddsEllipsisWhenTruncated()
    {
        string text = new string('a', 60) + " bảo hiểm " + new string('b', 60);
        string snip = SearchSnippetBuilder.Build(text, "bao hiem", contextChars: 10);
        Assert.StartsWith("...", snip);
        Assert.EndsWith("...", snip);
    }

    [Fact]
    public void Build_NoMatch_ReturnsNonEmptyHead()
    {
        string text = "Một đoạn văn bản dài để kiểm tra fallback";
        string snip = SearchSnippetBuilder.Build(text, "khongcotrongday");
        Assert.False(string.IsNullOrEmpty(snip));
    }

    [Fact]
    public void Build_EmptyText_ReturnsEmpty()
    {
        Assert.Equal("", SearchSnippetBuilder.Build("", "abc"));
    }

    [Fact]
    public void Build_ShortMatchNearStart_NoLeadingEllipsis()
    {
        string snip = SearchSnippetBuilder.Build("bảo hiểm nhân thọ", "bao hiem", contextChars: 40);
        Assert.False(snip.StartsWith("..."));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SearchSnippetBuilderTests"`
Expected: FAIL compile "type or namespace name 'SearchSnippetBuilder' could not be found".

- [ ] **Step 3: Implement `SearchSnippetBuilder`**

Tạo `src/PdfReaderApp/Services/SearchSnippetBuilder.cs`:

```csharp
using System;

namespace PdfReaderApp.Services;

/// <summary>
/// Dựng đoạn snippet hiển thị từ text GỐC (giữ dấu) quanh vị trí match,
/// dù index/match chạy trên text đã fold (bỏ dấu).
/// </summary>
public static class SearchSnippetBuilder
{
    public static string Build(string originalText, string query, int contextChars = 40)
    {
        if (string.IsNullOrEmpty(originalText)) return "";

        string foldedQuery = SearchNormalizer.Fold(query);
        var (foldedText, map) = SearchNormalizer.FoldWithMap(originalText);

        int hit = foldedQuery.Length == 0
            ? -1
            : foldedText.IndexOf(foldedQuery, StringComparison.Ordinal);

        if (hit < 0)
        {
            // Không định vị được match: trả phần đầu text gốc.
            if (originalText.Length <= contextChars * 2) return originalText.Trim();
            return originalText.Substring(0, contextChars * 2).Trim() + "...";
        }

        int srcStart = map[hit];
        int srcEnd = map[hit + foldedQuery.Length - 1] + 1; // exclusive
        int from = Math.Max(0, srcStart - contextChars);
        int to = Math.Min(originalText.Length, srcEnd + contextChars);

        string window = originalText.Substring(from, to - from).Trim();
        string prefix = from > 0 ? "..." : "";
        string suffix = to < originalText.Length ? "..." : "";
        return prefix + window + suffix;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SearchSnippetBuilderTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/SearchSnippetBuilder.cs tests/PdfReaderApp.Tests/Services/SearchSnippetBuilderTests.cs
git commit -m "feat: add SearchSnippetBuilder for diacritic-preserving snippets"
```

---

### Task 3: Trả snippet có dấu từ `SqliteDocumentIndex.SearchText`

**Files:**
- Modify: `src/PdfReaderApp/Services/SqliteDocumentIndex.cs:404-463` (method `SearchText`)
- Test: `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSnippetTests.cs`

**Interfaces:**
- Consumes: `SearchSnippetBuilder.Build(string, string)` (Task 2).
- Produces: `SearchText` trả `SearchResult` mà `Snippet` là text gốc có dấu quanh match (thay cho snippet folded). Shape không đổi.

- [ ] **Step 1: Write the failing test**

Tạo `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSnippetTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexSnippetTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;
    private const string DocId = "snippet-doc";

    public SqliteDocumentIndexSnippetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(
            Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _idx.WriteChunks(DocId, null, 1, new List<Chunk>
        {
            new(DocId, 0, 0, "Hợp đồng bảo hiểm có hiệu lực từ ngày ký kết")
        });
    }

    [Fact]
    public void SearchText_Snippet_PreservesDiacritics()
    {
        var results = _idx.SearchText(DocId, "bao hiem");
        var hit = results.First(r => r.PageIndex == 0);
        Assert.Contains("bảo hiểm", hit.Snippet);
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~SqliteDocumentIndexSnippetTests"`
Expected: FAIL — snippet hiện trả text folded "bao hiem" nên `Assert.Contains("bảo hiểm", ...)` thất bại.

- [ ] **Step 3: Đổi truy vấn để dựng snippet từ text gốc**

Trong `src/PdfReaderApp/Services/SqliteDocumentIndex.cs`, nhánh trigram (`phrase.Length >= 3`), thay khối SELECT + reader hiện tại (dòng ~424-439) bằng:

```csharp
                    using var cmd = _conn.CreateCommand();
                    cmd.CommandText = @"
SELECT c.page_index,
       c.text,
       c.chunk_id
FROM chunks_fts
JOIN chunks c ON c.chunk_id = chunks_fts.rowid
WHERE chunks_fts MATCH $q AND c.document_id = $id
ORDER BY rank
LIMIT $lim";
                    cmd.Parameters.AddWithValue("$q", matchExpr);
                    cmd.Parameters.AddWithValue("$id", documentId);
                    cmd.Parameters.AddWithValue("$lim", limit);

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        results.Add(new SearchResult(
                            r.GetInt32(0),
                            SearchSnippetBuilder.Build(r.GetString(1), query),
                            r.GetInt64(2)));
```

Và nhánh LIKE fallback (`else`, dòng ~444-454), thay bằng:

```csharp
                    using var cmd = _conn.CreateCommand();
                    cmd.CommandText =
                        "SELECT page_index, text, chunk_id FROM chunks " +
                        "WHERE document_id=$id AND search_text LIKE $p LIMIT $lim";
                    cmd.Parameters.AddWithValue("$id", documentId);
                    cmd.Parameters.AddWithValue("$p", "%" + phrase + "%");
                    cmd.Parameters.AddWithValue("$lim", limit);

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        results.Add(new SearchResult(
                            r.GetInt32(0),
                            SearchSnippetBuilder.Build(r.GetString(1), query),
                            r.GetInt64(2)));
```

(`query` là tham số raw của `SearchText`; `SearchSnippetBuilder.Build` tự fold. Không còn dùng `snippet(...)`.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SqliteDocumentIndex"`
Expected: PASS — bao gồm test mới và toàn bộ test phrase/trigram cũ (`SqliteDocumentIndexPhraseSearchTests`, `SqliteDocumentIndexSearchTests`, `SqliteDocumentIndexTrigramSearchTests`) vẫn xanh (chúng chỉ assert `PageIndex`, không phụ thuộc nội dung snippet).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/SqliteDocumentIndex.cs tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSnippetTests.cs
git commit -m "feat: return diacritic-preserving snippets from SqliteDocumentIndex.SearchText"
```

---

### Task 4: ViewModel — vòng đời highlight + lệnh clear

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs` (vùng property ~40-46 và command `Search`/`SelectSearchResult` ~249-273)
- Test: `tests/PdfReaderApp.Tests/MainViewModelTests.cs`

**Interfaces:**
- Consumes: nothing mới.
- Produces:
  - `public string ExecutedSearchQuery { get; set; }` (sinh từ `[ObservableProperty] private string _executedSearchQuery`) — query đã chạy, dùng cho UI bold.
  - `partial void OnSearchQueryChanged(string value)` — clear highlight mỗi khi query đổi; clear results + ExecutedSearchQuery khi rỗng.
  - `ClearSearchCommand` (sinh từ `[RelayCommand] private void ClearSearch()`) — đặt `SearchQuery = ""`.

- [ ] **Step 1: Write the failing tests**

Thêm vào `tests/PdfReaderApp.Tests/MainViewModelTests.cs` (thêm `using PdfReaderApp.Models;` ở đầu file):

```csharp
[Fact]
public void OnSearchQueryChanged_ClearsPageHighlight()
{
    var vm = new MainViewModel();
    vm.SelectedSearchQuery = "abc";
    vm.SearchQuery = "x";
    Assert.Equal(string.Empty, vm.SelectedSearchQuery);
}

[Fact]
public void SearchQuery_SetEmpty_ClearsResultsAndExecutedQuery()
{
    var vm = new MainViewModel();
    vm.ExecutedSearchQuery = "abc";
    vm.SearchQuery = "";
    Assert.Empty(vm.SearchResults);
    Assert.Equal(string.Empty, vm.ExecutedSearchQuery);
}

[Fact]
public void ClearSearchCommand_ClearsQueryAndResults()
{
    var vm = new MainViewModel();
    vm.SearchQuery = "abc";
    vm.ClearSearchCommand.Execute(null);
    Assert.Equal(string.Empty, vm.SearchQuery);
    Assert.Empty(vm.SearchResults);
}

[Fact]
public void SelectSearchResult_SetsPageAndHighlightQuery()
{
    var vm = new MainViewModel { SearchQuery = "hành" };
    var result = new SearchResult(2, "snip", 1);
    vm.SelectSearchResultCommand.Execute(result);
    Assert.Equal(3, vm.CurrentPage);
    Assert.Equal("hành", vm.SelectedSearchQuery);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: FAIL compile "does not contain a definition for 'ExecutedSearchQuery'" / "'ClearSearchCommand'".

- [ ] **Step 3: Thêm property, partial handler, command**

Trong `src/PdfReaderApp/ViewModels/MainViewModel.cs`, sau dòng `private string _selectedSearchQuery = string.Empty;` (dòng 46) thêm:

```csharp
    [ObservableProperty]
    private string _executedSearchQuery = string.Empty;

    // Mỗi khi người dùng sửa/xóa ô tìm: tắt highlight trên trang ngay lập tức.
    // Nếu ô rỗng: xóa luôn danh sách kết quả và query đã chạy.
    partial void OnSearchQueryChanged(string value)
    {
        SelectedSearchQuery = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            ExecutedSearchQuery = string.Empty;
        }
    }
```

Trong `Search()` (dòng ~250), thêm `ExecutedSearchQuery = SearchQuery;` ngay trước vòng `foreach`:

```csharp
        try
        {
            ExecutedSearchQuery = SearchQuery;
            foreach (var hit in _documentIndex.SearchText(_documentId, SearchQuery))
                SearchResults.Add(hit);
        }
```

Sau `SelectSearchResult` (sau dòng 273) thêm command clear:

```csharp
    [RelayCommand]
    private void ClearSearch() => SearchQuery = string.Empty;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~MainViewModelTests"`
Expected: PASS (toàn bộ test cũ + 4 test mới).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/ViewModels/MainViewModel.cs tests/PdfReaderApp.Tests/MainViewModelTests.cs
git commit -m "feat: clear page highlight when search query changes; add ClearSearch command"
```

---

### Task 5: `SearchSnippetHighlighter` — tách đoạn đậm + attached property

**Files:**
- Create: `src/PdfReaderApp/SearchSnippetHighlighter.cs` (namespace `PdfReaderApp`)
- Test: `tests/PdfReaderApp.Tests/Services/SearchSnippetHighlighterTests.cs`

**Interfaces:**
- Consumes: `SearchNormalizer.Fold(string)`, `SearchNormalizer.FoldWithMap(string)` (Task 1).
- Produces:
  - `public static IReadOnlyList<(string Text, bool IsMatch)> ComputeSegments(string text, string query)` — chia `text` thành đoạn thường/khớp (accent-insensitive); nối các `Text` lại bằng đúng `text`.
  - Attached properties `SearchSnippetHighlighter.Text` và `SearchSnippetHighlighter.Query` (cho `TextBlock`), dựng `Inlines` với `Run` đậm ở đoạn khớp.

- [ ] **Step 1: Write the failing tests**

Tạo `tests/PdfReaderApp.Tests/Services/SearchSnippetHighlighterTests.cs`:

```csharp
using System.Linq;
using PdfReaderApp;

namespace PdfReaderApp.Tests.Services;

public class SearchSnippetHighlighterTests
{
    [Fact]
    public void ComputeSegments_BoldsAccentInsensitiveMatch()
    {
        var segs = SearchSnippetHighlighter.ComputeSegments("Hợp đồng bảo hiểm", "bao hiem");
        Assert.Contains(segs, s => s.IsMatch && s.Text == "bảo hiểm");
    }

    [Fact]
    public void ComputeSegments_ConcatEqualsOriginal()
    {
        string text = "Hợp đồng bảo hiểm và bảo hiểm nhân thọ";
        var segs = SearchSnippetHighlighter.ComputeSegments(text, "bao hiem");
        Assert.Equal(text, string.Concat(segs.Select(s => s.Text)));
    }

    [Fact]
    public void ComputeSegments_MultipleOccurrences_BoldsEach()
    {
        var segs = SearchSnippetHighlighter.ComputeSegments("bảo hiểm và bảo hiểm", "bao hiem");
        Assert.Equal(2, segs.Count(s => s.IsMatch));
    }

    [Fact]
    public void ComputeSegments_EmptyQuery_SingleNonMatchSegment()
    {
        var segs = SearchSnippetHighlighter.ComputeSegments("Hợp đồng", "");
        Assert.Single(segs);
        Assert.False(segs[0].IsMatch);
        Assert.Equal("Hợp đồng", segs[0].Text);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter "FullyQualifiedName~SearchSnippetHighlighterTests"`
Expected: FAIL compile "type or namespace name 'SearchSnippetHighlighter' could not be found".

- [ ] **Step 3: Implement `SearchSnippetHighlighter`**

Tạo `src/PdfReaderApp/SearchSnippetHighlighter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PdfReaderApp.Services;

namespace PdfReaderApp;

/// <summary>
/// Tách snippet thành các đoạn thường/khớp (accent-insensitive) để in đậm từ khóa,
/// và expose attached property gắn vào TextBlock.
/// </summary>
public static class SearchSnippetHighlighter
{
    public static IReadOnlyList<(string Text, bool IsMatch)> ComputeSegments(string text, string query)
    {
        if (string.IsNullOrEmpty(text)) return new List<(string, bool)>();

        string fq = SearchNormalizer.Fold(query);
        if (fq.Length == 0) return new List<(string, bool)> { (text, false) };

        var (ft, map) = SearchNormalizer.FoldWithMap(text);
        var segs = new List<(string, bool)>();
        int srcPos = 0;
        int search = 0;
        while (search <= ft.Length - fq.Length)
        {
            int idx = ft.IndexOf(fq, search, StringComparison.Ordinal);
            if (idx < 0) break;

            int srcStart = map[idx];
            int srcEnd = map[idx + fq.Length - 1] + 1;
            if (srcStart < srcPos) srcStart = srcPos; // an toàn khi gộp whitespace

            if (srcStart > srcPos)
                segs.Add((text.Substring(srcPos, srcStart - srcPos), false));
            if (srcEnd > srcStart)
                segs.Add((text.Substring(srcStart, srcEnd - srcStart), true));

            srcPos = srcEnd;
            search = idx + fq.Length;
        }
        if (srcPos < text.Length)
            segs.Add((text.Substring(srcPos), false));

        return segs;
    }

    public static readonly DependencyProperty TextProperty =
        DependencyProperty.RegisterAttached("Text", typeof(string),
            typeof(SearchSnippetHighlighter), new PropertyMetadata(null, OnChanged));

    public static readonly DependencyProperty QueryProperty =
        DependencyProperty.RegisterAttached("Query", typeof(string),
            typeof(SearchSnippetHighlighter), new PropertyMetadata(null, OnChanged));

    public static void SetText(DependencyObject o, string v) => o.SetValue(TextProperty, v);
    public static string GetText(DependencyObject o) => (string)o.GetValue(TextProperty);
    public static void SetQuery(DependencyObject o, string v) => o.SetValue(QueryProperty, v);
    public static string GetQuery(DependencyObject o) => (string)o.GetValue(QueryProperty);

    private static void OnChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not TextBlock tb) return;
        string text = GetText(tb) ?? "";
        string query = GetQuery(tb) ?? "";
        tb.Inlines.Clear();
        foreach (var (segText, isMatch) in ComputeSegments(text, query))
        {
            var run = new Run(segText);
            if (isMatch) run.FontWeight = FontWeights.Bold;
            tb.Inlines.Add(run);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter "FullyQualifiedName~SearchSnippetHighlighterTests"`
Expected: PASS (4 tests).

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/SearchSnippetHighlighter.cs tests/PdfReaderApp.Tests/Services/SearchSnippetHighlighterTests.cs
git commit -m "feat: add SearchSnippetHighlighter to bold accent-insensitive matches"
```

---

### Task 6: MainWindow.xaml — popup to hơn, nút X, bind highlighter

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml:120-161` (vùng search box + popup)

**Interfaces:**
- Consumes: `ClearSearchCommand`, `ExecutedSearchQuery` (Task 4); attached property `local:SearchSnippetHighlighter.Text/.Query` (Task 5); snippet có dấu (Task 3).
- Produces: UI cuối cùng. Không có unit test (test thủ công).

- [ ] **Step 1: Thay khối search box + popup**

Trong `src/PdfReaderApp/MainWindow.xaml`, thay toàn bộ khối từ `<materialDesign:ColorZone Mode="Standard" CornerRadius="20" ...>` (dòng 121) tới `</Popup>` (dòng 160) bằng:

```xml
                        <materialDesign:ColorZone Mode="Standard" CornerRadius="20" materialDesign:ElevationAssist.Elevation="Dp0"
                                                  Background="{DynamicResource MaterialDesignTextFieldBoxBackground}" Padding="8,2">
                            <Grid>
                                <Grid.ColumnDefinitions>
                                    <ColumnDefinition Width="Auto" />
                                    <ColumnDefinition Width="*" />
                                    <ColumnDefinition Width="Auto" />
                                </Grid.ColumnDefinitions>
                                <materialDesign:PackIcon Kind="Search" VerticalAlignment="Center" Margin="8,0" Opacity=".5" />
                                <TextBox x:Name="SearchBox" Grid.Column="1" Width="220"
                                         materialDesign:HintAssist.Hint="Search..." BorderThickness="0"
                                         materialDesign:TextFieldAssist.DecorationVisibility="Collapsed"
                                         Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}">
                                    <TextBox.InputBindings>
                                        <KeyBinding Key="Enter" Command="{Binding SearchCommand}" />
                                    </TextBox.InputBindings>
                                </TextBox>
                                <Button Grid.Column="2" Style="{StaticResource MaterialDesignFlatButton}"
                                        Padding="4" MinWidth="0" VerticalAlignment="Center"
                                        ToolTip="Xóa tìm kiếm"
                                        Command="{Binding ClearSearchCommand}">
                                    <materialDesign:PackIcon Kind="Close" Opacity=".6" />
                                </Button>
                            </Grid>
                        </materialDesign:ColorZone>

                        <Popup IsOpen="{Binding SearchResults.Count, Converter={StaticResource CountToBoolConverter}, Mode=OneWay}"
                               PlacementTarget="{Binding ElementName=SearchBox}" Placement="Bottom"
                               StaysOpen="False" MaxHeight="420" Width="460">
                            <materialDesign:Card Padding="6" Background="{DynamicResource MaterialDesignPaper}">
                                <ScrollViewer VerticalScrollBarVisibility="Auto" MaxHeight="408">
                                    <ItemsControl ItemsSource="{Binding SearchResults}">
                                        <ItemsControl.ItemTemplate>
                                            <DataTemplate>
                                                <Button HorizontalContentAlignment="Left"
                                                        Padding="10,8"
                                                        Style="{StaticResource MaterialDesignFlatButton}"
                                                        Command="{Binding DataContext.SelectSearchResultCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                                        CommandParameter="{Binding}">
                                                    <StackPanel>
                                                        <TextBlock Text="{Binding PageIndex, Converter={StaticResource PageDisplayConverter}, StringFormat='Trang {0}'}"
                                                                   FontWeight="Bold" FontSize="13"
                                                                   Foreground="{DynamicResource PrimaryHueMidBrush}"/>
                                                        <TextBlock TextWrapping="Wrap" FontSize="14" Opacity="0.9" Margin="0,2,0,0"
                                                                   local:SearchSnippetHighlighter.Text="{Binding Snippet}"
                                                                   local:SearchSnippetHighlighter.Query="{Binding DataContext.ExecutedSearchQuery, RelativeSource={RelativeSource AncestorType=ItemsControl}}"/>
                                                    </StackPanel>
                                                </Button>
                                            </DataTemplate>
                                        </ItemsControl.ItemTemplate>
                                    </ItemsControl>
                                </ScrollViewer>
                            </materialDesign:Card>
                        </Popup>
```

(`local:` đã khai báo `xmlns:local="clr-namespace:PdfReaderApp"` ở dòng 7 — `SearchSnippetHighlighter` nằm cùng namespace nên dùng được ngay.)

- [ ] **Step 2: Build để chắc XAML hợp lệ**

Run: `dotnet build PdfReaderApp.slnx`
Expected: Build succeeded, không lỗi XAML/binding compile.

- [ ] **Step 3: Test thủ công**

Run: `dotnet run --project src/PdfReaderApp`
Kiểm tra:
1. Mở 1 PDF tiếng Việt; gõ từ khóa (vd "bao hiem") rồi Enter.
2. Popup hiện TO hơn (rộng ~460), snippet dài, CÓ DẤU ("bảo hiểm"), từ khóa IN ĐẬM.
3. Click 1 kết quả -> nhảy tới đầu trang đó, từ khóa được tô VÀNG trên trang.
4. Sửa nội dung ô search -> highlight vàng biến mất ngay.
5. Nhấn nút X -> ô search trống, popup đóng, highlight biến mất.
6. Gõ query mới rồi search lại -> highlight cũ không còn sót.

- [ ] **Step 4: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml
git commit -m "feat: enlarge search popup, bold keywords, add clear (X) button"
```

---

## Self-Review

**1. Spec coverage:**
- "Popup to hơn + snippet dài" → Task 6 (Width 460, MaxHeight 420, FontSize 14, ScrollViewer).
- "Snippet có dấu" → Task 1 + 2 + 3.
- "Từ khóa in đậm" → Task 5 + 6.
- "Nút X clear" → Task 4 (`ClearSearchCommand`) + Task 6.
- "Highlight tắt khi query đổi/rỗng/X" → Task 4 (`OnSearchQueryChanged`).
- "Click → đầu trang + tô vàng mọi match" → giữ nguyên (`SelectSearchResult` + `DrawHighlights`), xác nhận ở Task 4 test + Task 6 manual.
- Out-of-scope (không cuộn tới dòng match, không màu riêng occurrence, không bbox) → không có task, đúng ý đồ.

**2. Placeholder scan:** Không có TBD/TODO; mọi step có code/lệnh/kết quả mong đợi cụ thể.

**3. Type consistency:** `FoldWithMap` (Task 1) trả `(string folded, int[] map)` — dùng nhất quán ở Task 2 và Task 5. `SearchSnippetBuilder.Build(string, string, int=40)` — gọi đúng ở Task 3. `ComputeSegments(string, string)` trả `IReadOnlyList<(string Text, bool IsMatch)>` — dùng nhất quán Task 5 test + attached property. `ExecutedSearchQuery`, `ClearSearchCommand` (Task 4) — bind đúng tên ở Task 6. `SearchResult(PageIndex, Snippet, ChunkId)` không đổi shape.
