# SQLite RAG + Search (Sub-project 2) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Index PDF text into SQLite (FTS5 + sqlite-vec embeddings), power the toolbar Search (list → jump → highlight), and replace SP1's per-page chat context with semantic RAG retrieval — all behind the unchanged `AskStreamingAsync(question, context)` boundary.

**Architecture:** A shared SQLite DB (`%APPDATA%\PdfReaderApp\index.db`) holds one row per chunk in three linked structures: `chunks` (canonical), `chunks_fts` (FTS5 keyword search), `vec_chunks` (sqlite-vec KNN). `DocumentIndexingService` chunks the cached `TextBlock`s, embeds them via OpenAI (Microsoft.Extensions.AI), and writes the DB in the background on file open. `RagContextService` embeds a question and returns the nearest chunks as chat context, falling back to SP1's `DocumentContextBuilder` when the index is not ready.

**Tech Stack:** Microsoft.Data.Sqlite + SQLite FTS5 + sqlite-vec (vec0 native), Microsoft.Extensions.AI embeddings + OpenAI, CommunityToolkit.Mvvm, SkiaSharp (highlight overlay), xUnit.

## Global Constraints

- Target framework: `net10.0-windows`; Nullable: enabled
- No `Co-Authored-By` trailer in any commit; no `--no-verify`
- Do NOT add or commit `conductor/`, `.serena/`, or `obj/`,`bin/` (now untracked)
- Never hardcode the API key; read only from `ISettingsService` (SP1)
- Embedding model: `text-embedding-3-small` (1536 dims)
- Chunking: group by page, ~900 chars per chunk, ~100 char overlap
- One shared DB at `%APPDATA%\PdfReaderApp\index.db`; `document_id` = SHA256 of file bytes
- RAG retrieval: vector-only, `k=5`; context capped at SP1's 48000 chars
- `documents.status` values (exact): `indexing`, `complete`, `partial`, `text-only`, `empty`
- **Native/version note:** sqlite-vec (`vec0`) DDL/KNN syntax (partition-key column, `MATCH ... k=`) and the Microsoft.Extensions.AI embedding members (`AsIEmbeddingGenerator`, `GeneratedEmbeddings`, `Embedding<float>.Vector`) vary by version. The code below targets sqlite-vec ≥0.1.6 and M.E.AI 9.x; if a member/syntax differs in the restored package, use the IntelliSense/docs equivalent — builds and unit tests surface mismatches. Risk is confined to `SqliteDocumentIndex` and `OpenAiEmbeddingGeneratorFactory`.

---

## File Structure

| File | Action | Responsibility |
|---|---|---|
| `src/PdfReaderApp/PdfReaderApp.csproj` | Modify | Add Microsoft.Data.Sqlite; copy `native/vec0.dll` to output |
| `native/vec0.dll` | Add (binary) | sqlite-vec Windows x64 prebuilt |
| `src/PdfReaderApp/Models/Chunk.cs` | Create | Chunk + SearchResult + IndexingProgress + DocumentIndexStatus |
| `src/PdfReaderApp/Services/DocumentId.cs` | Create | SHA256 file → document_id |
| `src/PdfReaderApp/Services/TextChunker.cs` | Create | TextBlocks → chunks (char window + overlap) |
| `src/PdfReaderApp/Services/IDocumentIndex.cs` | Create | SQLite facade contract |
| `src/PdfReaderApp/Services/SqliteDocumentIndex.cs` | Create | schema, write, FTS5 search, vec0 KNN |
| `src/PdfReaderApp/Services/IEmbeddingGeneratorFactory.cs` | Create | Build IEmbeddingGenerator from key |
| `src/PdfReaderApp/Services/OpenAiEmbeddingGeneratorFactory.cs` | Create | OpenAI-backed impl |
| `src/PdfReaderApp/Services/DocumentIndexingService.cs` | Create | Orchestrate chunk→embed→store (background) |
| `src/PdfReaderApp/Services/RagContextService.cs` | Create | Embed question → KNN → context (or null) |
| `src/PdfReaderApp/ViewModels/MainViewModel.cs` | Modify | Index on open, search command, chat uses RAG + fallback |
| `src/PdfReaderApp/Controls/PdfViewerControl.xaml(.cs)` | Modify | Highlight overlay layer |
| `src/PdfReaderApp/MainWindow.xaml` | Modify | Bind search box + results panel |
| `tests/PdfReaderApp.Tests/Services/*` | Create | Unit tests per task |

---

### Task 1: SQLite dependency + models + DocumentId

**Files:**
- Modify: `src/PdfReaderApp/PdfReaderApp.csproj`
- Create: `src/PdfReaderApp/Models/Chunk.cs`
- Create: `src/PdfReaderApp/Services/DocumentId.cs`
- Test: `tests/PdfReaderApp.Tests/Services/DocumentIdTests.cs`

**Interfaces:**
- Produces:
  - `record Chunk(string DocumentId, int PageIndex, int Ordinal, string Text)`
  - `record SearchResult(int PageIndex, string Snippet, long ChunkId)`
  - `record IndexingProgress(int Done, int Total, string Status)`
  - `enum DocumentIndexStatus { None, Indexing, Complete, Partial, TextOnly, Empty }`
  - `static string DocumentId.FromBytes(byte[] bytes)`, `static string DocumentId.FromFile(string path)`

- [ ] **Step 1: Add Microsoft.Data.Sqlite package**

```bash
cd src/PdfReaderApp
dotnet add package Microsoft.Data.Sqlite
cd ../..
```

- [ ] **Step 2: Create models**

Create `src/PdfReaderApp/Models/Chunk.cs`:

```csharp
namespace PdfReaderApp.Models;

public sealed record Chunk(string DocumentId, int PageIndex, int Ordinal, string Text);

public sealed record SearchResult(int PageIndex, string Snippet, long ChunkId);

public sealed record IndexingProgress(int Done, int Total, string Status);

public enum DocumentIndexStatus
{
    None,
    Indexing,
    Complete,
    Partial,
    TextOnly,
    Empty
}
```

- [ ] **Step 3: Write the failing DocumentId test**

Create `tests/PdfReaderApp.Tests/Services/DocumentIdTests.cs`:

```csharp
using System.Text;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class DocumentIdTests
{
    [Fact]
    public void FromBytes_SameBytes_SameId()
    {
        var a = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        var b = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        Assert.Equal(a, b);
    }

    [Fact]
    public void FromBytes_DifferentBytes_DifferentId()
    {
        var a = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        var b = DocumentId.FromBytes(Encoding.UTF8.GetBytes("world"));
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void FromBytes_IsHex64_Sha256()
    {
        var a = DocumentId.FromBytes(Encoding.UTF8.GetBytes("hello"));
        Assert.Equal(64, a.Length);
        Assert.All(a, c => Assert.Contains(c, "0123456789abcdef"));
    }
}
```

- [ ] **Step 4: Run test — verify it FAILS**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `DocumentId` not found.

- [ ] **Step 5: Implement DocumentId**

Create `src/PdfReaderApp/Services/DocumentId.cs`:

```csharp
using System.IO;
using System.Security.Cryptography;

namespace PdfReaderApp.Services;

public static class DocumentId
{
    public static string FromBytes(byte[] bytes)
    {
        byte[] hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string FromFile(string path) => FromBytes(File.ReadAllBytes(path));
}
```

- [ ] **Step 6: Run test — verify it PASSES**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~DocumentIdTests" -v normal
```

Expected: 3 tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/PdfReaderApp/PdfReaderApp.csproj src/PdfReaderApp/Models/Chunk.cs \
        src/PdfReaderApp/Services/DocumentId.cs tests/PdfReaderApp.Tests/Services/DocumentIdTests.cs
git commit -m "feat: add SQLite dep, index models, and DocumentId hashing"
```

---

### Task 2: TextChunker

**Files:**
- Create: `src/PdfReaderApp/Services/TextChunker.cs`
- Test: `tests/PdfReaderApp.Tests/Services/TextChunkerTests.cs`

**Interfaces:**
- Consumes: `TextBlock` (Phase 1: `Text`, 0-based `PageIndex`), `Chunk` (Task 1)
- Produces: `static List<Chunk> TextChunker.Chunk(string documentId, IReadOnlyList<TextBlock> blocks, int maxChars = 900, int overlap = 100)`

**Background:** Group blocks by page (preserving order), join each page's text with spaces, then slide a window of `maxChars` with `overlap` carried into the next window. Chunks never span pages (so `page_index` is unambiguous for jump/highlight). `ordinal` is the running index across the whole document.

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/TextChunkerTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class TextChunkerTests
{
    private static TextBlock B(string text, int page) =>
        new(text, 0f, 0f, 0f, 0f, 12f, page, "Paragraph");

    [Fact]
    public void Chunk_ShortSinglePage_ProducesOneChunk()
    {
        var blocks = new List<TextBlock> { B("hello", 0), B("world", 0) };
        var chunks = TextChunker.Chunk("doc", blocks);

        Assert.Single(chunks);
        Assert.Equal(0, chunks[0].PageIndex);
        Assert.Contains("hello", chunks[0].Text);
        Assert.Contains("world", chunks[0].Text);
    }

    [Fact]
    public void Chunk_Empty_ReturnsEmpty()
    {
        var chunks = TextChunker.Chunk("doc", new List<TextBlock>());
        Assert.Empty(chunks);
    }

    [Fact]
    public void Chunk_NeverSpansPages()
    {
        var blocks = new List<TextBlock> { B(new string('a', 50), 0), B(new string('b', 50), 1) };
        var chunks = TextChunker.Chunk("doc", blocks, maxChars: 900, overlap: 100);

        Assert.All(chunks, c => Assert.True(c.Text.All(ch => ch == 'a') || c.Text.All(ch => ch == 'b') || c.Text.Trim().Length == 0));
        Assert.Contains(chunks, c => c.PageIndex == 0);
        Assert.Contains(chunks, c => c.PageIndex == 1);
    }

    [Fact]
    public void Chunk_LongPage_SplitsWithOverlap()
    {
        var blocks = new List<TextBlock> { B(new string('x', 2000), 0) };
        var chunks = TextChunker.Chunk("doc", blocks, maxChars: 900, overlap: 100);

        Assert.True(chunks.Count >= 2);
        Assert.All(chunks, c => Assert.True(c.Text.Length <= 900));
        // ordinals are sequential from 0
        Assert.Equal(Enumerable.Range(0, chunks.Count).ToList(), chunks.Select(c => c.Ordinal).ToList());
    }

    [Fact]
    public void Chunk_AssignsDocumentId()
    {
        var chunks = TextChunker.Chunk("mydoc", new List<TextBlock> { B("hi", 0) });
        Assert.All(chunks, c => Assert.Equal("mydoc", c.DocumentId));
    }
}
```

- [ ] **Step 2: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `TextChunker` not found.

- [ ] **Step 3: Implement TextChunker**

Create `src/PdfReaderApp/Services/TextChunker.cs`:

```csharp
using System.Linq;
using System.Text;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public static class TextChunker
{
    public static List<Chunk> Chunk(
        string documentId, IReadOnlyList<TextBlock> blocks, int maxChars = 900, int overlap = 100)
    {
        var result = new List<Chunk>();
        int ordinal = 0;

        foreach (var pageGroup in blocks.GroupBy(b => b.PageIndex).OrderBy(g => g.Key))
        {
            int pageIndex = pageGroup.Key;
            string pageText = string.Join(" ", pageGroup.Select(b => b.Text)).Trim();
            if (pageText.Length == 0) continue;

            int start = 0;
            while (start < pageText.Length)
            {
                int len = Math.Min(maxChars, pageText.Length - start);
                string slice = pageText.Substring(start, len);
                result.Add(new Chunk(documentId, pageIndex, ordinal++, slice));

                if (start + len >= pageText.Length) break;
                start += Math.Max(1, maxChars - overlap);
            }
        }

        return result;
    }
}
```

- [ ] **Step 4: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~TextChunkerTests" -v normal
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/TextChunker.cs tests/PdfReaderApp.Tests/Services/TextChunkerTests.cs
git commit -m "feat: add TextChunker (per-page char window with overlap)"
```

---

### Task 3: sqlite-vec native lib + IDocumentIndex + schema + chunk writing

**Files:**
- Add: `native/vec0.dll` (sqlite-vec Windows x64 prebuilt)
- Modify: `src/PdfReaderApp/PdfReaderApp.csproj`
- Create: `src/PdfReaderApp/Services/IDocumentIndex.cs`
- Create: `src/PdfReaderApp/Services/SqliteDocumentIndex.cs`
- Test: `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSchemaTests.cs`

**Interfaces:**
- Consumes: `Chunk`, `DocumentIndexStatus` (Task 1)
- Produces:
  - `interface IDocumentIndex : IDisposable` with:
    - `void EnsureSchema()`
    - `DocumentIndexStatus GetStatus(string documentId, string embeddingModel)`
    - `void DeleteDocument(string documentId)`
    - `IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)` (sets status `indexing`, writes `documents`+`chunks`+`chunks_fts`, returns assigned chunk_ids in order)
    - `void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings)`
    - `void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)`
    - `List<SearchResult> SearchText(string documentId, string query, int limit = 50)` (Task 4)
    - `List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5)` (Task 5)
  - `SqliteDocumentIndex(string dbPath, string vec0Path)` — opens connection, loads vec0, can `EnsureSchema()`

**Background — loading vec0:** Microsoft.Data.Sqlite loads native extensions via `connection.EnableExtensions(true)` then `connection.LoadExtension(vec0Path)`. The dll must exist at `vec0Path`. FTS5 is built into the bundled `e_sqlite3`, so no extension needed for it.

- [ ] **Step 1: Obtain vec0.dll and wire it into the build**

Download the sqlite-vec Windows x86_64 prebuilt (`vec0.dll`) and place it at `native/vec0.dll`. Attempt automatically (PowerShell):

```powershell
$ver = "v0.1.6"
$tmp = New-Item -ItemType Directory -Force -Path "$env:TEMP\sqlitevec"
$url = "https://github.com/asg017/sqlite-vec/releases/download/$ver/sqlite-vec-0.1.6-loadable-windows-x86_64.tar.gz"
Invoke-WebRequest -Uri $url -OutFile "$tmp\vec.tar.gz"
tar -xzf "$tmp\vec.tar.gz" -C $tmp
New-Item -ItemType Directory -Force -Path "native" | Out-Null
Copy-Item "$tmp\vec0.dll" "native\vec0.dll" -Force
```

If the asset name differs, check https://github.com/asg017/sqlite-vec/releases for the current Windows x86_64 loadable asset. **If the download cannot be performed in this environment, report this task as BLOCKED** (the rest of SP2 depends on `vec0.dll`).

Add to `src/PdfReaderApp/PdfReaderApp.csproj` inside a new `<ItemGroup>`:

```xml
<ItemGroup>
  <None Include="..\..\native\vec0.dll">
    <Link>vec0.dll</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

> `native/vec0.dll` is a binary asset (not gitignored). Commit it so CI/other machines have it.

- [ ] **Step 2: Write the failing schema/write test**

Create `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSchemaTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexSchemaTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly string _vec0Path;

    public SqliteDocumentIndexSchemaTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "test.db");
        // vec0.dll is copied next to the test assembly via the project reference's output.
        _vec0Path = Path.Combine(AppContext.BaseDirectory, "vec0.dll");
    }

    private SqliteDocumentIndex NewIndex()
    {
        var idx = new SqliteDocumentIndex(_dbPath, _vec0Path);
        idx.EnsureSchema();
        return idx;
    }

    [Fact]
    public void EnsureSchema_CreatesTables_NoThrow()
    {
        using var idx = NewIndex();
        // EnsureSchema is idempotent
        idx.EnsureSchema();
    }

    [Fact]
    public void WriteChunks_ThenGetStatus_IsIndexing()
    {
        using var idx = NewIndex();
        var chunks = new List<Chunk>
        {
            new("doc1", 0, 0, "alpha text"),
            new("doc1", 1, 1, "beta text")
        };
        var ids = idx.WriteChunks("doc1", "C:\\a.pdf", pageCount: 2, chunks);

        Assert.Equal(2, ids.Count);
        Assert.Equal(DocumentIndexStatus.Indexing, idx.GetStatus("doc1", "text-embedding-3-small"));
    }

    [Fact]
    public void GetStatus_UnknownDocument_IsNone()
    {
        using var idx = NewIndex();
        Assert.Equal(DocumentIndexStatus.None, idx.GetStatus("nope", "text-embedding-3-small"));
    }

    [Fact]
    public void DeleteDocument_RemovesChunks()
    {
        using var idx = NewIndex();
        idx.WriteChunks("doc1", null, 1, new List<Chunk> { new("doc1", 0, 0, "x") });
        idx.DeleteDocument("doc1");
        Assert.Equal(DocumentIndexStatus.None, idx.GetStatus("doc1", "text-embedding-3-small"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 3: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `SqliteDocumentIndex` / `IDocumentIndex` not found.

- [ ] **Step 4: Create IDocumentIndex**

Create `src/PdfReaderApp/Services/IDocumentIndex.cs`:

```csharp
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public interface IDocumentIndex : IDisposable
{
    void EnsureSchema();
    DocumentIndexStatus GetStatus(string documentId, string embeddingModel);
    void DeleteDocument(string documentId);
    IReadOnlyList<long> WriteChunks(
        string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks);
    void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings);
    void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel);
    List<SearchResult> SearchText(string documentId, string query, int limit = 50);
    List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5);
}
```

- [ ] **Step 5: Implement SqliteDocumentIndex (schema + write + status; search/KNN stubbed for Tasks 4-5)**

Create `src/PdfReaderApp/Services/SqliteDocumentIndex.cs`:

```csharp
using System.Globalization;
using System.Linq;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class SqliteDocumentIndex : IDocumentIndex
{
    private const string EmbeddingDim = "1536";
    private readonly SqliteConnection _conn;
    private readonly object _lock = new();
    private bool _vecAvailable;

    /// <summary>True when sqlite-vec loaded; false → vector RAG degrades, FTS5 search still works.</summary>
    public bool VectorSearchAvailable => _vecAvailable;

    public SqliteDocumentIndex(string dbPath, string vec0Path)
    {
        _conn = new SqliteConnection($"Data Source={dbPath}");
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL;");
        try
        {
            _conn.EnableExtensions(true);
            _conn.LoadExtension(vec0Path);
            _vecAvailable = true;
        }
        catch (Exception)
        {
            _vecAvailable = false; // degrade: FTS5 only, RAG falls back to per-page context
        }
    }

    public void EnsureSchema()
    {
        lock (_lock)
        {
            Exec(@"
CREATE TABLE IF NOT EXISTS documents (
  document_id TEXT PRIMARY KEY, file_path TEXT, page_count INTEGER,
  chunk_count INTEGER, embedding_model TEXT, status TEXT, indexed_at INTEGER);
CREATE TABLE IF NOT EXISTS chunks (
  chunk_id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL,
  page_index INTEGER NOT NULL, ordinal INTEGER NOT NULL, text TEXT NOT NULL);
CREATE INDEX IF NOT EXISTS idx_chunks_doc ON chunks(document_id);
CREATE VIRTUAL TABLE IF NOT EXISTS chunks_fts USING fts5(
  text, content='chunks', content_rowid='chunk_id');");
            if (_vecAvailable)
                Exec(@"CREATE VIRTUAL TABLE IF NOT EXISTS vec_chunks USING vec0(
  document_id TEXT partition key, chunk_id INTEGER PRIMARY KEY, embedding FLOAT[" + EmbeddingDim + "]);");
        }
    }

    public DocumentIndexStatus GetStatus(string documentId, string embeddingModel)
    {
        lock (_lock)
        {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "SELECT status, embedding_model FROM documents WHERE document_id=$id";
        cmd.Parameters.AddWithValue("$id", documentId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return DocumentIndexStatus.None;

        string status = r.GetString(0);
        string model = r.IsDBNull(1) ? "" : r.GetString(1);
        // Model mismatch on a completed index means it must be rebuilt.
        if (status == "complete" && model != embeddingModel) return DocumentIndexStatus.None;

        return status switch
        {
            "indexing" => DocumentIndexStatus.Indexing,
            "complete" => DocumentIndexStatus.Complete,
            "partial" => DocumentIndexStatus.Partial,
            "text-only" => DocumentIndexStatus.TextOnly,
            "empty" => DocumentIndexStatus.Empty,
            _ => DocumentIndexStatus.None
        };
        }
    }

    public void DeleteDocument(string documentId)
    {
        lock (_lock)
        {
        var deletes = new List<string>
        {
            "DELETE FROM chunks_fts WHERE rowid IN (SELECT chunk_id FROM chunks WHERE document_id=$id)",
            "DELETE FROM chunks WHERE document_id=$id",
            "DELETE FROM documents WHERE document_id=$id"
        };
        if (_vecAvailable)
            deletes.Insert(1, "DELETE FROM vec_chunks WHERE document_id=$id");

        using var tx = _conn.BeginTransaction();
        foreach (var sql in deletes)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = sql;
            cmd.Parameters.AddWithValue("$id", documentId);
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        }
    }

    public IReadOnlyList<long> WriteChunks(
        string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)
    {
        DeleteDocument(documentId); // idempotent re-index (takes _lock internally)

        lock (_lock)
        {
        var ids = new List<long>();
        using var tx = _conn.BeginTransaction();

        using (var doc = _conn.CreateCommand())
        {
            doc.Transaction = tx;
            doc.CommandText = @"INSERT INTO documents
                (document_id, file_path, page_count, chunk_count, embedding_model, status, indexed_at)
                VALUES ($id,$path,$pc,$cc,NULL,'indexing',$ts)";
            doc.Parameters.AddWithValue("$id", documentId);
            doc.Parameters.AddWithValue("$path", (object?)filePath ?? DBNull.Value);
            doc.Parameters.AddWithValue("$pc", pageCount);
            doc.Parameters.AddWithValue("$cc", chunks.Count);
            doc.Parameters.AddWithValue("$ts", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            doc.ExecuteNonQuery();
        }

        foreach (var c in chunks)
        {
            long id;
            using (var cmd = _conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"INSERT INTO chunks (document_id, page_index, ordinal, text)
                    VALUES ($d,$p,$o,$t); SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$d", c.DocumentId);
                cmd.Parameters.AddWithValue("$p", c.PageIndex);
                cmd.Parameters.AddWithValue("$o", c.Ordinal);
                cmd.Parameters.AddWithValue("$t", c.Text);
                id = (long)cmd.ExecuteScalar()!;
            }
            using (var fts = _conn.CreateCommand())
            {
                fts.Transaction = tx;
                fts.CommandText = "INSERT INTO chunks_fts (rowid, text) VALUES ($id,$t)";
                fts.Parameters.AddWithValue("$id", id);
                fts.Parameters.AddWithValue("$t", c.Text);
                fts.ExecuteNonQuery();
            }
            ids.Add(id);
        }

        tx.Commit();
        return ids;
        }
    }

    public void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings)
    {
        if (!_vecAvailable) return; // degrade: no vector store available
        lock (_lock)
        {
        using var tx = _conn.BeginTransaction();
        foreach (var (chunkId, vector) in embeddings)
        {
            using var cmd = _conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO vec_chunks (document_id, chunk_id, embedding)
                VALUES ((SELECT document_id FROM chunks WHERE chunk_id=$id), $id, $vec)";
            cmd.Parameters.AddWithValue("$id", chunkId);
            cmd.Parameters.AddWithValue("$vec", ToJson(vector));
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
        }
    }

    public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)
    {
        lock (_lock)
        {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = "UPDATE documents SET status=$s, embedding_model=$m WHERE document_id=$id";
        cmd.Parameters.AddWithValue("$s", status switch
        {
            DocumentIndexStatus.TextOnly => "text-only",
            _ => status.ToString().ToLowerInvariant()
        });
        cmd.Parameters.AddWithValue("$m", embeddingModel);
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.ExecuteNonQuery();
        }
    }

    // Task 4 implements this (wrap body in lock (_lock)).
    public List<SearchResult> SearchText(string documentId, string query, int limit = 50)
        => throw new NotImplementedException();

    // Task 5 implements this (wrap body in lock (_lock); return empty if !_vecAvailable).
    public List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5)
        => throw new NotImplementedException();

    internal static string ToJson(float[] vector) =>
        "[" + string.Join(",", vector.Select(f => f.ToString(CultureInfo.InvariantCulture))) + "]";

    private void Exec(string sql)
    {
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        lock (_lock) { _conn.Dispose(); }
    }
}
```

> If `LoadExtension`/`EnableExtensions` are unavailable or the vec0 DDL is rejected, that is the version signal — adjust to the restored sqlite-vec syntax. The `SetStatus` mapping converts the `TextOnly` enum to the `text-only` string constant.

- [ ] **Step 6: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~SqliteDocumentIndexSchemaTests" -v normal
```

Expected: 4 tests pass. If they error with "vec0 not found", confirm `vec0.dll` was copied to the test output (`AppContext.BaseDirectory`); add the same `<None Include="..\..\native\vec0.dll">` copy item to `tests/PdfReaderApp.Tests/PdfReaderApp.Tests.csproj`.

- [ ] **Step 7: Commit**

```bash
git add native/vec0.dll src/PdfReaderApp/PdfReaderApp.csproj \
        src/PdfReaderApp/Services/IDocumentIndex.cs src/PdfReaderApp/Services/SqliteDocumentIndex.cs \
        tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSchemaTests.cs \
        tests/PdfReaderApp.Tests/PdfReaderApp.Tests.csproj
git commit -m "feat: add SqliteDocumentIndex schema, chunk writing, and vec0 loading"
```

---

### Task 4: FTS5 SearchText

**Files:**
- Modify: `src/PdfReaderApp/Services/SqliteDocumentIndex.cs` (implement `SearchText`)
- Test: `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSearchTests.cs`

**Interfaces:**
- Produces: working `SqliteDocumentIndex.SearchText(documentId, query, limit)` → `List<SearchResult>` ordered by FTS5 rank, filtered to `documentId`

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSearchTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexSearchTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;

    public SqliteDocumentIndexSearchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _idx.WriteChunks("doc1", null, 3, new List<Chunk>
        {
            new("doc1", 0, 0, "the quick brown fox"),
            new("doc1", 1, 1, "lazy dog sleeps"),
            new("doc1", 2, 2, "the fox jumps high")
        });
        _idx.WriteChunks("doc2", null, 1, new List<Chunk>
        {
            new("doc2", 0, 0, "unrelated fox content")
        });
    }

    [Fact]
    public void SearchText_FindsMatchingChunks_WithPageIndex()
    {
        var results = _idx.SearchText("doc1", "fox");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains(r.PageIndex, new[] { 0, 2 }));
    }

    [Fact]
    public void SearchText_FiltersByDocument()
    {
        var results = _idx.SearchText("doc1", "fox");
        // doc2's "unrelated fox content" must not appear
        Assert.DoesNotContain(results, r => r.Snippet.Contains("unrelated"));
    }

    [Fact]
    public void SearchText_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(_idx.SearchText("doc1", "elephant"));
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run tests — verify they FAIL**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~SqliteDocumentIndexSearchTests" -v normal
```

Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Implement SearchText**

In `src/PdfReaderApp/Services/SqliteDocumentIndex.cs`, replace the `SearchText` stub:

```csharp
    public List<SearchResult> SearchText(string documentId, string query, int limit = 50)
    {
        lock (_lock)
        {
        var results = new List<SearchResult>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT c.page_index,
       snippet(chunks_fts, 0, '[', ']', '...', 12) AS snip,
       c.chunk_id
FROM chunks_fts
JOIN chunks c ON c.chunk_id = chunks_fts.rowid
WHERE chunks_fts MATCH $q AND c.document_id = $id
ORDER BY rank
LIMIT $lim";
        cmd.Parameters.AddWithValue("$q", query);
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.Parameters.AddWithValue("$lim", limit);

        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new SearchResult(r.GetInt32(0), r.GetString(1), r.GetInt64(2)));

        return results;
        }
    }
```

- [ ] **Step 4: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~SqliteDocumentIndexSearchTests" -v normal
```

Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/SqliteDocumentIndex.cs \
        tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexSearchTests.cs
git commit -m "feat: implement FTS5 SearchText on SqliteDocumentIndex"
```

---

### Task 5: vec0 KNN RetrieveRelevant

**Files:**
- Modify: `src/PdfReaderApp/Services/SqliteDocumentIndex.cs` (implement `RetrieveRelevant`)
- Test: `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexKnnTests.cs`

**Interfaces:**
- Produces: working `SqliteDocumentIndex.RetrieveRelevant(documentId, queryVector, k)` → `List<Chunk>` ordered nearest-first, filtered to `documentId`

**Background:** Embeddings were written in Task 3 via `WriteEmbeddings`. KNN uses sqlite-vec's `MATCH`+`k` with the `document_id` partition key. Tests build deterministic 1536-dim vectors (mostly zeros, a single "hot" dimension) so cosine ordering is predictable without calling OpenAI.

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexKnnTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexKnnTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;

    private static float[] Hot(int dim)
    {
        var v = new float[1536];
        v[dim] = 1f;
        return v;
    }

    public SqliteDocumentIndexKnnTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        var ids = _idx.WriteChunks("doc1", null, 3, new List<Chunk>
        {
            new("doc1", 0, 0, "chunk hot-0"),
            new("doc1", 1, 1, "chunk hot-1"),
            new("doc1", 2, 2, "chunk hot-2")
        });
        _idx.WriteEmbeddings(new List<(long, float[])>
        {
            (ids[0], Hot(0)), (ids[1], Hot(1)), (ids[2], Hot(2))
        });
    }

    [Fact]
    public void RetrieveRelevant_ReturnsNearestFirst()
    {
        // query closest to dimension 1 -> chunk "hot-1" should rank first
        var results = _idx.RetrieveRelevant("doc1", Hot(1), k: 3);

        Assert.NotEmpty(results);
        Assert.Contains("hot-1", results[0].Text);
    }

    [Fact]
    public void RetrieveRelevant_RespectsK()
    {
        var results = _idx.RetrieveRelevant("doc1", Hot(0), k: 2);
        Assert.True(results.Count <= 2);
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
```

- [ ] **Step 2: Run tests — verify they FAIL**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~SqliteDocumentIndexKnnTests" -v normal
```

Expected: FAIL with `NotImplementedException`.

- [ ] **Step 3: Implement RetrieveRelevant**

In `src/PdfReaderApp/Services/SqliteDocumentIndex.cs`, replace the `RetrieveRelevant` stub:

```csharp
    public List<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5)
    {
        if (!_vecAvailable) return new List<Chunk>(); // degrade: caller falls back to per-page context
        lock (_lock)
        {
        var results = new List<Chunk>();
        using var cmd = _conn.CreateCommand();
        cmd.CommandText = @"
SELECT c.document_id, c.page_index, c.ordinal, c.text
FROM vec_chunks v
JOIN chunks c ON c.chunk_id = v.chunk_id
WHERE v.document_id = $id AND v.embedding MATCH $vec AND k = $k
ORDER BY distance";
        cmd.Parameters.AddWithValue("$id", documentId);
        cmd.Parameters.AddWithValue("$vec", ToJson(queryVector));
        cmd.Parameters.AddWithValue("$k", k);

        using var r = cmd.ExecuteReader();
        while (r.Read())
            results.Add(new Chunk(r.GetString(0), r.GetInt32(1), r.GetInt32(2), r.GetString(3)));

        return results;
        }
    }
```

> Exact vec0 KNN syntax (`MATCH`/`k`/partition filter/`distance` column) may differ by version. If the query errors, consult the installed sqlite-vec docs and adjust this single method. Tests confirm correct ordering once syntax is right.

- [ ] **Step 4: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~SqliteDocumentIndexKnnTests" -v normal
```

Expected: 2 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/SqliteDocumentIndex.cs \
        tests/PdfReaderApp.Tests/Services/SqliteDocumentIndexKnnTests.cs
git commit -m "feat: implement vec0 KNN RetrieveRelevant on SqliteDocumentIndex"
```

---

### Task 6: Embedding factory + DocumentIndexingService

**Files:**
- Create: `src/PdfReaderApp/Services/IEmbeddingGeneratorFactory.cs`
- Create: `src/PdfReaderApp/Services/OpenAiEmbeddingGeneratorFactory.cs`
- Create: `src/PdfReaderApp/Services/DocumentIndexingService.cs`
- Test: `tests/PdfReaderApp.Tests/Services/DocumentIndexingServiceTests.cs`

**Interfaces:**
- Consumes: `IDocumentIndex`, `TextChunker`, `Chunk`, `ISettingsService` (SP1), `TextBlock`, `Microsoft.Extensions.AI` (`IEmbeddingGenerator<string, Embedding<float>>`)
- Produces:
  - `interface IEmbeddingGeneratorFactory { IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey); }`
  - `class DocumentIndexingService` with ctor `(IDocumentIndex index, IEmbeddingGeneratorFactory embedFactory, ISettingsService settings)` and:
    - `const string EmbeddingModel = "text-embedding-3-small"`
    - `Task IndexAsync(string documentId, string? filePath, IReadOnlyList<TextBlock> blocks, IProgress<IndexingProgress>? progress, CancellationToken ct)`

- [ ] **Step 1: Create the embedding factory interface**

Create `src/PdfReaderApp/Services/IEmbeddingGeneratorFactory.cs`:

```csharp
using Microsoft.Extensions.AI;

namespace PdfReaderApp.Services;

public interface IEmbeddingGeneratorFactory
{
    IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey);
}
```

- [ ] **Step 2: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/DocumentIndexingServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class DocumentIndexingServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _k;
        public FakeSettings(string? k) => _k = k;
        public string? GetApiKey() => _k;
        public void SaveApiKey(string apiKey) { }
        public bool HasApiKey() => !string.IsNullOrEmpty(_k);
    }

    private sealed class FakeEmbedFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) => new FakeGen();
    }

    private sealed class FakeGen : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var list = values.Select(_ =>
            {
                var v = new float[1536];
                v[0] = 1f;
                return new Embedding<float>(v);
            }).ToList();
            return Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(list));
        }
        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    // Records what the service asked the index to do.
    private sealed class RecordingIndex : IDocumentIndex
    {
        public List<Chunk> Written = new();
        public List<(long, float[])> Embeddings = new();
        public string? FinalStatusDoc;
        public DocumentIndexStatus FinalStatus;

        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => DocumentIndexStatus.None;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)
        {
            Written.AddRange(chunks);
            return Enumerable.Range(1, chunks.Count).Select(i => (long)i).ToList();
        }
        public void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> e) => Embeddings.AddRange(e);
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel)
        { FinalStatusDoc = documentId; FinalStatus = status; }
        public List<SearchResult> SearchText(string d, string q, int l = 50) => new();
        public List<Chunk> RetrieveRelevant(string d, float[] v, int k = 5) => new();
        public void Dispose() { }
    }

    private static List<TextBlock> Blocks(params string[] texts) =>
        texts.Select((t, i) => new TextBlock(t, 0, 0, 0, 0, 12, i, "Paragraph")).ToList();

    [Fact]
    public async Task IndexAsync_WithKey_WritesChunksEmbeddingsAndCompletes()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", "a.pdf", Blocks("page zero text", "page one text"), null, CancellationToken.None);

        Assert.NotEmpty(index.Written);
        Assert.Equal(index.Written.Count, index.Embeddings.Count);
        Assert.Equal(DocumentIndexStatus.Complete, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_NoKey_WritesChunksButTextOnly()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings(null));

        await svc.IndexAsync("doc1", null, Blocks("hello world"), null, CancellationToken.None);

        Assert.NotEmpty(index.Written);
        Assert.Empty(index.Embeddings);
        Assert.Equal(DocumentIndexStatus.TextOnly, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_NoText_SetsEmpty()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));

        await svc.IndexAsync("doc1", null, new List<TextBlock>(), null, CancellationToken.None);

        Assert.Equal(DocumentIndexStatus.Empty, index.FinalStatus);
    }

    [Fact]
    public async Task IndexAsync_ReportsProgress()
    {
        var index = new RecordingIndex();
        var svc = new DocumentIndexingService(index, new FakeEmbedFactory(), new FakeSettings("sk-x"));
        var reports = new List<IndexingProgress>();
        var progress = new Progress<IndexingProgress>(p => reports.Add(p));

        await svc.IndexAsync("doc1", null, Blocks(new string('a', 2000)), progress, CancellationToken.None);

        // Progress is captured asynchronously; allow the sync context to drain.
        await Task.Delay(50);
        Assert.NotEmpty(reports);
    }
}
```

- [ ] **Step 3: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `DocumentIndexingService` / `IEmbeddingGeneratorFactory` not found.

- [ ] **Step 4: Implement the OpenAI factory and the service**

Create `src/PdfReaderApp/Services/OpenAiEmbeddingGeneratorFactory.cs`:

```csharp
using Microsoft.Extensions.AI;
using OpenAI;

namespace PdfReaderApp.Services;

public sealed class OpenAiEmbeddingGeneratorFactory : IEmbeddingGeneratorFactory
{
    private const string Model = "text-embedding-3-small";

    public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) =>
        new OpenAIClient(apiKey).GetEmbeddingClient(Model).AsIEmbeddingGenerator();
}
```

> If `GetEmbeddingClient(...).AsIEmbeddingGenerator()` differs in the restored M.E.AI.OpenAI version, use the IntelliSense equivalent (e.g. `AsEmbeddingGenerator`). Build-verified.

Create `src/PdfReaderApp/Services/DocumentIndexingService.cs`:

```csharp
using System.Linq;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class DocumentIndexingService
{
    public const string EmbeddingModel = "text-embedding-3-small";
    private const int BatchSize = 100;

    private readonly IDocumentIndex _index;
    private readonly IEmbeddingGeneratorFactory _embedFactory;
    private readonly ISettingsService _settings;

    public DocumentIndexingService(
        IDocumentIndex index, IEmbeddingGeneratorFactory embedFactory, ISettingsService settings)
    {
        _index = index;
        _embedFactory = embedFactory;
        _settings = settings;
    }

    public async Task IndexAsync(
        string documentId, string? filePath, IReadOnlyList<TextBlock> blocks,
        IProgress<IndexingProgress>? progress, CancellationToken ct)
    {
        var chunks = TextChunker.Chunk(documentId, blocks);
        int pageCount = blocks.Count == 0 ? 0 : blocks.Max(b => b.PageIndex) + 1;

        if (chunks.Count == 0)
        {
            _index.WriteChunks(documentId, filePath, pageCount, chunks);
            _index.SetStatus(documentId, DocumentIndexStatus.Empty, EmbeddingModel);
            return;
        }

        var chunkIds = _index.WriteChunks(documentId, filePath, pageCount, chunks);
        progress?.Report(new IndexingProgress(0, chunks.Count, "indexing"));

        string? key = _settings.GetApiKey();
        if (string.IsNullOrEmpty(key))
        {
            _index.SetStatus(documentId, DocumentIndexStatus.TextOnly, EmbeddingModel);
            return;
        }

        var generator = _embedFactory.Create(key);
        int done = 0;

        for (int i = 0; i < chunks.Count; i += BatchSize)
        {
            ct.ThrowIfCancellationRequested();

            var batchChunks = chunks.Skip(i).Take(BatchSize).ToList();
            var batchIds = chunkIds.Skip(i).Take(BatchSize).ToList();

            var embeddings = await generator.GenerateAsync(
                batchChunks.Select(c => c.Text), cancellationToken: ct);

            var pairs = new List<(long, float[])>();
            for (int j = 0; j < batchChunks.Count; j++)
                pairs.Add((batchIds[j], embeddings[j].Vector.ToArray()));

            _index.WriteEmbeddings(pairs);
            done += batchChunks.Count;
            progress?.Report(new IndexingProgress(done, chunks.Count, "indexing"));
        }

        _index.SetStatus(documentId, DocumentIndexStatus.Complete, EmbeddingModel);
        progress?.Report(new IndexingProgress(done, chunks.Count, "complete"));
    }
}
```

> `GeneratedEmbeddings<Embedding<float>>` is indexable (`embeddings[j]`) and each `Embedding<float>` exposes `.Vector` (`ReadOnlyMemory<float>`). If member names differ, adjust per the version note.

- [ ] **Step 5: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~DocumentIndexingServiceTests" -v normal
```

Expected: 4 tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/Services/IEmbeddingGeneratorFactory.cs \
        src/PdfReaderApp/Services/OpenAiEmbeddingGeneratorFactory.cs \
        src/PdfReaderApp/Services/DocumentIndexingService.cs \
        tests/PdfReaderApp.Tests/Services/DocumentIndexingServiceTests.cs
git commit -m "feat: add embedding factory and DocumentIndexingService orchestration"
```

---

### Task 7: RagContextService

**Files:**
- Create: `src/PdfReaderApp/Services/RagContextService.cs`
- Test: `tests/PdfReaderApp.Tests/Services/RagContextServiceTests.cs`

**Interfaces:**
- Consumes: `IDocumentIndex`, `IEmbeddingGeneratorFactory`, `ISettingsService`, `DocumentIndexingService.EmbeddingModel`
- Produces: `class RagContextService` ctor `(IDocumentIndex index, IEmbeddingGeneratorFactory embedFactory, ISettingsService settings)` with:
  - `Task<string?> BuildContextAsync(string documentId, string question, int k = 5, int maxChars = 48000, CancellationToken ct = default)` — returns joined nearest-chunk text, or `null` when not retrievable (index not complete, no key, no results)

- [ ] **Step 1: Write the failing tests**

Create `tests/PdfReaderApp.Tests/Services/RagContextServiceTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class RagContextServiceTests
{
    private sealed class FakeSettings : ISettingsService
    {
        private readonly string? _k;
        public FakeSettings(string? k) => _k = k;
        public string? GetApiKey() => _k;
        public void SaveApiKey(string a) { }
        public bool HasApiKey() => !string.IsNullOrEmpty(_k);
    }
    private sealed class FakeGen : IEmbeddingGenerator<string, Embedding<float>>
    {
        public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? o = null, CancellationToken ct = default)
            => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                values.Select(_ => new Embedding<float>(new float[1536])).ToList()));
        public object? GetService(Type t, object? k = null) => null;
        public void Dispose() { }
    }
    private sealed class FakeFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(string apiKey) => new FakeGen();
    }
    private sealed class StubIndex : IDocumentIndex
    {
        private readonly DocumentIndexStatus _status;
        private readonly List<Chunk> _retrieve;
        public StubIndex(DocumentIndexStatus s, List<Chunk> retrieve) { _status = s; _retrieve = retrieve; }
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m) => _status;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c) => new List<long>();
        public void WriteEmbeddings(IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { }
        public List<SearchResult> SearchText(string d, string q, int l = 50) => new();
        public List<Chunk> RetrieveRelevant(string d, float[] v, int k = 5) => _retrieve;
        public void Dispose() { }
    }

    [Fact]
    public async Task BuildContext_Complete_ReturnsJoinedChunks()
    {
        var idx = new StubIndex(DocumentIndexStatus.Complete, new List<Chunk>
        {
            new("d", 0, 0, "first chunk"), new("d", 1, 1, "second chunk")
        });
        var svc = new RagContextService(idx, new FakeFactory(), new FakeSettings("sk-x"));

        var ctx = await svc.BuildContextAsync("d", "câu hỏi");

        Assert.NotNull(ctx);
        Assert.Contains("first chunk", ctx);
        Assert.Contains("second chunk", ctx);
    }

    [Fact]
    public async Task BuildContext_NotComplete_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.TextOnly, new()), new FakeFactory(), new FakeSettings("sk-x"));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }

    [Fact]
    public async Task BuildContext_NoKey_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.Complete, new()), new FakeFactory(), new FakeSettings(null));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }

    [Fact]
    public async Task BuildContext_NoResults_ReturnsNull()
    {
        var svc = new RagContextService(
            new StubIndex(DocumentIndexStatus.Complete, new()), new FakeFactory(), new FakeSettings("sk-x"));
        Assert.Null(await svc.BuildContextAsync("d", "q"));
    }
}
```

- [ ] **Step 2: Run tests — verify they FAIL**

```bash
dotnet build tests/PdfReaderApp.Tests 2>&1 | grep "error" | head -5
```

Expected: `RagContextService` not found.

- [ ] **Step 3: Implement RagContextService**

Create `src/PdfReaderApp/Services/RagContextService.cs`:

```csharp
using System.Linq;
using System.Text;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class RagContextService
{
    private readonly IDocumentIndex _index;
    private readonly IEmbeddingGeneratorFactory _embedFactory;
    private readonly ISettingsService _settings;

    public RagContextService(
        IDocumentIndex index, IEmbeddingGeneratorFactory embedFactory, ISettingsService settings)
    {
        _index = index;
        _embedFactory = embedFactory;
        _settings = settings;
    }

    public async Task<string?> BuildContextAsync(
        string documentId, string question, int k = 5, int maxChars = 48000, CancellationToken ct = default)
    {
        if (_index.GetStatus(documentId, DocumentIndexingService.EmbeddingModel) != DocumentIndexStatus.Complete)
            return null;

        string? key = _settings.GetApiKey();
        if (string.IsNullOrEmpty(key)) return null;

        var generator = _embedFactory.Create(key);
        var embeddings = await generator.GenerateAsync(new[] { question }, cancellationToken: ct);
        float[] queryVector = embeddings[0].Vector.ToArray();

        var chunks = _index.RetrieveRelevant(documentId, queryVector, k);
        if (chunks.Count == 0) return null;

        var sb = new StringBuilder();
        foreach (var c in chunks)
        {
            string line = $"[Trang {c.PageIndex + 1}] {c.Text}";
            if (sb.Length + line.Length > maxChars) break;
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(line);
        }
        return sb.ToString();
    }
}
```

- [ ] **Step 4: Run tests — verify they PASS**

```bash
dotnet test tests/PdfReaderApp.Tests --filter "FullyQualifiedName~RagContextServiceTests" -v normal
```

Expected: 4 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/PdfReaderApp/Services/RagContextService.cs \
        tests/PdfReaderApp.Tests/Services/RagContextServiceTests.cs
git commit -m "feat: add RagContextService (embed question, KNN, build context or null)"
```

---

### Task 8: MainViewModel integration (index on open, RAG chat, search command)

**Files:**
- Modify: `src/PdfReaderApp/ViewModels/MainViewModel.cs`

**Interfaces:**
- Consumes: `IDocumentIndex`+`SqliteDocumentIndex`, `DocumentIndexingService`, `RagContextService`, `OpenAiEmbeddingGeneratorFactory`, `DocumentId`, `DocumentContextBuilder` (SP1 fallback), `AiChatService` (SP1), `SearchResult`, `IndexingProgress`
- Produces: `MainViewModel` wired for background indexing on open, RAG-or-fallback chat context, `SearchCommand` + `SearchResults` + `SelectSearchResultCommand`, `IndexingStatusText`

**Note:** No unit tests — composition/UI glue, build + manual. Builds on SP1's `MainViewModel` (which has `_documentService`, `_settingsService`, `_chatService`, `_documentBlocks`, streaming `SendMessage`, `OpenSettings`). This task assumes SP1 is already implemented on this branch.

- [ ] **Step 1: Add index fields and composition**

In `src/PdfReaderApp/ViewModels/MainViewModel.cs`, add usings and fields, and extend both constructors. Add near the other fields:

```csharp
    private readonly IDocumentIndex _documentIndex;
    private readonly DocumentIndexingService _indexingService;
    private readonly RagContextService _ragContext;

    private string? _documentId;
    private CancellationTokenSource? _indexCts;

    [ObservableProperty]
    private string _indexingStatusText = string.Empty;

    public ObservableCollection<SearchResult> SearchResults { get; } = new();

    [ObservableProperty]
    private string _searchQuery = string.Empty;
```

Add a private static helper to build the shared index DB path and replace the parameterless constructor:

```csharp
    private static string IndexDbPath()
    {
        string dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PdfReaderApp");
        System.IO.Directory.CreateDirectory(dir);
        return System.IO.Path.Combine(dir, "index.db");
    }

    public MainViewModel()
        : this(new ITextPdfDocumentService(),
               new WindowsSettingsService(),
               new OpenAiChatClientFactory(),
               new SqliteDocumentIndex(IndexDbPath(),
                   System.IO.Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
               new OpenAiEmbeddingGeneratorFactory())
    { }
```

Replace the injected constructor signature/body to accept the new collaborators (keep the existing SP1 body, add index wiring):

```csharp
    public MainViewModel(
        IPdfDocumentService documentService,
        ISettingsService settingsService,
        IChatClientFactory chatClientFactory,
        IDocumentIndex documentIndex,
        IEmbeddingGeneratorFactory embeddingFactory)
    {
        _documentService = documentService;
        _settingsService = settingsService;
        _analyzer = new PdfStructureAnalyzer(_documentService);
        _chatService = new AiChatService(settingsService, chatClientFactory);

        _documentIndex = documentIndex;
        _documentIndex.EnsureSchema();
        _indexingService = new DocumentIndexingService(_documentIndex, embeddingFactory, settingsService);
        _ragContext = new RagContextService(_documentIndex, embeddingFactory, settingsService);

        ChatMessages.Add(new ChatMessage
        {
            Role = "AI",
            Content = "Xin chào! Tôi có thể giúp gì cho bạn về tài liệu này?"
        });
    }
```

- [ ] **Step 2: Trigger background indexing in OpenFile**

In `OpenFile`, after the existing successful `LoadFile` + `_documentBlocks = _analyzer.AnalyzeRich()`, add:

```csharp
            _documentId = DocumentId.FromFile(FilePath);
            _chatService.ResetConversation();
            SearchResults.Clear();
            StartBackgroundIndexing();
```

Add the indexing launcher method:

```csharp
    private void StartBackgroundIndexing()
    {
        if (_documentId is null) return;

        _indexCts?.Cancel();
        _indexCts = new CancellationTokenSource();
        var ct = _indexCts.Token;
        string docId = _documentId;
        string? path = FilePath;
        var blocks = _documentBlocks;

        var progress = new Progress<IndexingProgress>(p =>
            IndexingStatusText = p.Status == "complete"
                ? string.Empty
                : $"Đang lập chỉ mục: {p.Done}/{p.Total}");

        _ = Task.Run(async () =>
        {
            try { await _indexingService.IndexAsync(docId, path, blocks, progress, ct); }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Windows.Application.Current?.Dispatcher.Invoke(() =>
                    IndexingStatusText = $"Lập chỉ mục lỗi: {ex.Message}");
            }
        }, ct);
    }
```

- [ ] **Step 3: Use RAG context (with SP1 fallback) in SendMessage**

In `SendMessage`, replace the line that builds `context` (SP1 used `DocumentContextBuilder.BuildAround(...)`) with:

```csharp
        string context;
        if (_documentId is not null)
        {
            string? rag = null;
            try { rag = await _ragContext.BuildContextAsync(_documentId, question); }
            catch { rag = null; }
            context = rag ?? DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);
        }
        else
        {
            context = DocumentContextBuilder.BuildAround(_documentBlocks, CurrentPage, ContextPageWindow);
        }
```

- [ ] **Step 4: Add Search and SelectSearchResult commands**

Add to the class:

```csharp
    [RelayCommand]
    private void Search()
    {
        SearchResults.Clear();
        if (_documentId is null || string.IsNullOrWhiteSpace(SearchQuery)) return;

        try
        {
            foreach (var hit in _documentIndex.SearchText(_documentId, SearchQuery))
                SearchResults.Add(hit);
        }
        catch (Exception ex)
        {
            System.Windows.MessageBox.Show($"Tìm kiếm lỗi: {ex.Message}", "Search",
                System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private void SelectSearchResult(SearchResult? result)
    {
        if (result is null) return;
        CurrentPage = result.PageIndex + 1; // CurrentPage is 1-based; highlight overlay (Task 9) reacts to this
        SelectedSearchQuery = SearchQuery;
    }

    [ObservableProperty]
    private string _selectedSearchQuery = string.Empty;
```

- [ ] **Step 5: Dispose the index**

In `Dispose`, add index cleanup alongside the SP1 `_documentService.Dispose()`:

```csharp
    public void Dispose()
    {
        _indexCts?.Cancel();
        _documentIndex.Dispose();
        _documentService.Dispose();
    }
```

- [ ] **Step 6: Build and run all tests**

```bash
dotnet build src/PdfReaderApp/PdfReaderApp.csproj
dotnet test tests/PdfReaderApp.Tests -v normal
```

Expected: 0 build errors; all unit tests (SP1 + SP2 Tasks 1-7) pass.

- [ ] **Step 7: Manual smoke check**

Run `dotnet run --project src/PdfReaderApp`. Open a PDF → status shows "Đang lập chỉ mục: …" then clears. With a key set, ask a question → answer reflects retrieved chunks ("[Trang N]"). Without a key → chat falls back, no crash.

- [ ] **Step 8: Commit**

```bash
git add src/PdfReaderApp/ViewModels/MainViewModel.cs
git commit -m "feat: index on open, RAG chat context with fallback, and search command"
```

---

### Task 9: Search results UI + highlight overlay

**Files:**
- Modify: `src/PdfReaderApp/MainWindow.xaml` (search box binding + results panel)
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml` (overlay layer)
- Modify: `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs` (draw highlight rects)

**Interfaces:**
- Consumes: `MainViewModel.SearchQuery`, `SearchCommand`, `SearchResults`, `SelectSearchResultCommand`, `SelectedSearchQuery`, `CurrentPage`; `PdfCoordinateMapper` (Phase 1); cached `TextBlock` bounds
- Produces: search results dropdown + on-page yellow highlight rectangles for the current page

**Note:** No unit tests — WPF view + Skia drawing. Build + manual. Read `PdfViewerControl.xaml.cs` first to match its existing Skia render loop and how it exposes the current page's render transform (scale/dpi/page height) before adding the overlay.

- [ ] **Step 1: Bind the search box and add a results dropdown**

In `src/PdfReaderApp/MainWindow.xaml`, replace the existing search `TextBox` (around lines 111-112, inside the right `StackPanel`) so it binds and triggers search on Enter, and add a results `Popup` below it:

```xml
<StackPanel Orientation="Horizontal" DockPanel.Dock="Right" HorizontalAlignment="Right">
    <materialDesign:ColorZone Mode="Standard" CornerRadius="20" materialDesign:ElevationAssist.Elevation="Dp0"
                              Background="{DynamicResource MaterialDesignTextFieldBoxBackground}" Padding="8,2">
        <Grid>
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="Auto" />
                <ColumnDefinition Width="*" />
            </Grid.ColumnDefinitions>
            <materialDesign:PackIcon Kind="Search" VerticalAlignment="Center" Margin="8,0" Opacity=".5" />
            <TextBox x:Name="SearchBox" Grid.Column="1" Width="150"
                     materialDesign:HintAssist.Hint="Search..." BorderThickness="0"
                     materialDesign:TextFieldAssist.DecorationVisibility="Collapsed"
                     Text="{Binding SearchQuery, UpdateSourceTrigger=PropertyChanged}">
                <TextBox.InputBindings>
                    <KeyBinding Key="Enter" Command="{Binding SearchCommand}" />
                </TextBox.InputBindings>
            </TextBox>
        </Grid>
    </materialDesign:ColorZone>

    <Popup IsOpen="{Binding SearchResults.Count, Converter={StaticResource CountToBoolConverter}}"
           PlacementTarget="{Binding ElementName=SearchBox}" Placement="Bottom"
           StaysOpen="False" MaxHeight="300" Width="320">
        <materialDesign:Card Padding="4" Background="{DynamicResource MaterialDesignPaper}">
            <ItemsControl ItemsSource="{Binding SearchResults}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Button HorizontalContentAlignment="Left"
                                Style="{StaticResource MaterialDesignFlatButton}"
                                Command="{Binding DataContext.SelectSearchResultCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
                                CommandParameter="{Binding}">
                            <StackPanel>
                                <TextBlock Text="{Binding PageIndex, StringFormat='Trang {0}'}" FontWeight="Bold" FontSize="11"/>
                                <TextBlock Text="{Binding Snippet}" TextWrapping="Wrap" FontSize="11" Opacity="0.8"/>
                            </StackPanel>
                        </Button>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </materialDesign:Card>
    </Popup>
</StackPanel>
```

Add a `CountToBoolConverter` (file `src/PdfReaderApp/Converters/CountToBoolConverter.cs`) and register it in `MainWindow.xaml` resources next to the existing converters:

```csharp
using System;
using System.Globalization;
using System.Windows.Data;

namespace PdfReaderApp;

public sealed class CountToBoolConverter : IValueConverter
{
    public object Convert(object value, Type t, object p, CultureInfo c) => value is int n && n > 0;
    public object ConvertBack(object value, Type t, object p, CultureInfo c) => throw new NotSupportedException();
}
```

Register (in `<Window.Resources>` of `MainWindow.xaml`, alongside `RoleToBrushConverter`):

```xml
<local:CountToBoolConverter x:Key="CountToBoolConverter" />
```

> Snippet shows `PageIndex` 0-based here for simplicity of binding; `SelectSearchResult` already converts to 1-based `CurrentPage`. If you want the label 1-based, add a converter — optional polish.

- [ ] **Step 2: Read the control, then add a highlight overlay layer**

Read `src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs` to find the Skia paint surface element and the per-page render transform. Add to `PdfViewerControl.xaml` a transparent `Canvas` (or reuse the existing `SKElement`) on top of the page surface to draw highlights, and expose two dependency properties on the control:

```csharp
public static readonly DependencyProperty HighlightQueryProperty =
    DependencyProperty.Register(nameof(HighlightQuery), typeof(string), typeof(PdfViewerControl),
        new PropertyMetadata(string.Empty, OnHighlightChanged));

public string HighlightQuery
{
    get => (string)GetValue(HighlightQueryProperty);
    set => SetValue(HighlightQueryProperty, value);
}

private static void OnHighlightChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    => ((PdfViewerControl)d).InvalidateVisual();
```

Bind in `MainWindow.xaml` on the `PdfViewerControl`:

```xml
HighlightQuery="{Binding SelectedSearchQuery}"
```

- [ ] **Step 3: Draw highlight rectangles in the Skia paint handler**

In the control's Skia paint method (the existing `OnPaintSurface`/equivalent found in Step 2), after the page is drawn, when `HighlightQuery` is non-empty, draw yellow semi-transparent rects over matching text on the visible page. Use the page's `TextBlock` bounds and the page render transform (the control already computes scale/dpi to render the page; reuse that to build a `PdfCoordinateMapper`):

```csharp
// Pseudocode to adapt to the control's actual render state:
// foreach visible page:
//   var mapper = new PdfCoordinateMapper(pageHeightPt, currentScale, currentDpi);
//   foreach (var b in blocksForThisPage)
//       if (b.Text.Contains(HighlightQuery, StringComparison.OrdinalIgnoreCase))
//       {
//           var (rx, ry) = mapper.PdfPointToRender(b.PdfX, b.PdfY + b.Height); // top-left of glyph box
//           var rect = SKRect.Create(rx + pageOffsetX, ry + pageOffsetY,
//                                     b.Width * pixelsPerPoint, b.Height * pixelsPerPoint);
//           canvas.DrawRect(rect, new SKPaint { Color = new SKColor(255, 235, 59, 110) });
//       }
```

The control needs the page's `TextBlock`s. The simplest path: add a `DependencyProperty` `IReadOnlyList<TextBlock> Blocks` on the control, bind it to a `MainViewModel.DocumentBlocks` getter (expose `_documentBlocks`), and filter by the page being painted. Wire it the same way as `HighlightQuery`.

- [ ] **Step 4: Build and run all tests**

```bash
dotnet build src/PdfReaderApp/PdfReaderApp.csproj
dotnet test tests/PdfReaderApp.Tests -v normal
```

Expected: 0 build errors; all unit tests pass.

- [ ] **Step 5: Manual end-to-end smoke check**

Run `dotnet run --project src/PdfReaderApp`. Open a PDF, wait for indexing. Type a keyword in the toolbar search → results dropdown lists pages + snippets. Click a result → viewer jumps to that page and the matching text is highlighted yellow. Ask a question → streamed answer cites "[Trang N]".

- [ ] **Step 6: Commit**

```bash
git add src/PdfReaderApp/MainWindow.xaml src/PdfReaderApp/Converters/CountToBoolConverter.cs \
        src/PdfReaderApp/Controls/PdfViewerControl.xaml src/PdfReaderApp/Controls/PdfViewerControl.xaml.cs
git commit -m "feat: add search results dropdown and on-page highlight overlay"
```

---

## Done

After Task 9:
- PDF text indexed into SQLite on open (background); reused across sessions via file-hash `document_id`
- Toolbar Search (FTS5): results list → jump to page → yellow highlight on page
- Chat uses semantic RAG (vec0 KNN top-5) with graceful fallback to SP1 per-page context
- ~24 new unit tests (DocumentId 3 + TextChunker 5 + schema 4 + FTS5 3 + KNN 2 + indexing 4 + RAG 4 + plus SP1/Phase 1 still green)
- Boundary `AskStreamingAsync(question, context)` unchanged; SP1 fallback intact
- Out-of-scope (future): hybrid retrieval, OCR, old-index cleanup, embedding-model picker in Settings
