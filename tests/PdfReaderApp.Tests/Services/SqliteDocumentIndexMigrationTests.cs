using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

// Ghim hành vi di trú schema cũ lên FTS5 trigram và các nhánh của SearchText.
public sealed class SqliteDocumentIndexMigrationTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dbPath;
    private readonly string _vec0Path;

    public SqliteDocumentIndexMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "legacy.db");
        _vec0Path = Path.Combine(AppContext.BaseDirectory, "vec0.dll");
    }

    private void SeedLegacyDb(bool withSearchTextColumn, bool trigramFts)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        string searchTextColumn = withSearchTextColumn ? ", search_text TEXT" : "";
        string tokenize = trigramFts ? ", tokenize='trigram'" : "";
        string searchTextFtsColumn = withSearchTextColumn ? "search_text" : "text";
        // user_version = 4 để RefoldSearchTextIfStale và WipeIfPreV4 không đụng vào dữ liệu.
        string sql = $@"
CREATE TABLE documents (
  document_id TEXT PRIMARY KEY, file_path TEXT, page_count INTEGER,
  chunk_count INTEGER, embedding_model TEXT, status TEXT, indexed_at INTEGER);
CREATE TABLE chunks (
  chunk_id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL,
  page_index INTEGER NOT NULL, ordinal INTEGER NOT NULL, text TEXT NOT NULL{searchTextColumn});
CREATE VIRTUAL TABLE chunks_fts USING fts5({searchTextFtsColumn}, content='chunks', content_rowid='chunk_id'{tokenize});
INSERT INTO chunks (document_id, page_index, ordinal, text) VALUES ('doc1', 0, 0, 'Kinh Hạnh phúc');
INSERT INTO chunks (document_id, page_index, ordinal, text) VALUES ('doc1', 1, 1, 'Kinh Thánh');
PRAGMA user_version = 4;";
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private string ScalarString(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }

    private SqliteDocumentIndex Migrate()
    {
        var idx = new SqliteDocumentIndex(_dbPath, _vec0Path);
        idx.EnsureSchema();
        return idx;
    }

    [Fact]
    public void EnsureSchema_LegacyDbWithoutSearchTextColumn_AddsColumnAndTrigramFts()
    {
        SeedLegacyDb(withSearchTextColumn: false, trigramFts: false);

        using (Migrate())
        {
        }

        Assert.Equal("1", ScalarString(
            "SELECT COUNT(*) FROM pragma_table_info('chunks') WHERE name='search_text'"));
        Assert.Contains("trigram", ScalarString("SELECT sql FROM sqlite_master WHERE name='chunks_fts'"),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EnsureSchema_LegacyDb_BackfillsFoldedSearchTextForExistingRows()
    {
        SeedLegacyDb(withSearchTextColumn: false, trigramFts: false);

        using (Migrate())
        {
        }

        Assert.Equal("kinh hanh phuc", ScalarString("SELECT search_text FROM chunks WHERE page_index=0"));
        Assert.Equal("2", ScalarString("SELECT COUNT(*) FROM chunks"));
    }

    [Fact]
    public void EnsureSchema_LegacyDb_SearchFindsBackfilledRowsAccentInsensitive()
    {
        SeedLegacyDb(withSearchTextColumn: false, trigramFts: false);

        using var idx = Migrate();
        var hits = idx.SearchText("doc1", "kinh hanh");

        var hit = Assert.Single(hits);
        Assert.Equal(0, hit.PageIndex);
    }

    [Fact]
    public void EnsureSchema_LegacyDbWithColumnButNonTrigramFts_RebuildsFtsAsTrigram()
    {
        SeedLegacyDb(withSearchTextColumn: true, trigramFts: false);

        using var idx = Migrate();

        Assert.Contains("trigram", ScalarString("SELECT sql FROM sqlite_master WHERE name='chunks_fts'"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Single(idx.SearchText("doc1", "thanh"));
    }

    [Fact]
    public void EnsureSchema_AlreadyMigratedDb_LeavesRowsUntouched()
    {
        SeedLegacyDb(withSearchTextColumn: true, trigramFts: true);
        ExecuteRaw("UPDATE chunks SET search_text='sentinel'");

        using (Migrate())
        {
        }

        Assert.Equal("sentinel", ScalarString("SELECT search_text FROM chunks WHERE page_index=0"));
    }

    [Fact]
    public void EnsureSchema_OlderUserVersion_RefoldsRowsAndStampsVersion4()
    {
        // Refold chạy trước và đóng dấu v4, nên WipeIfPreV4 phía sau không xoá gì.
        SeedLegacyDb(withSearchTextColumn: true, trigramFts: true);
        ExecuteRaw("UPDATE chunks SET search_text='stale'");
        ExecuteRaw("PRAGMA user_version = 3");

        using (Migrate())
        {
        }

        Assert.Equal("2", ScalarString("SELECT COUNT(*) FROM chunks"));
        Assert.Equal("kinh hanh phuc", ScalarString("SELECT search_text FROM chunks WHERE page_index=0"));
        Assert.Equal("4", ScalarString("PRAGMA user_version"));
    }

    [Fact]
    public void SearchText_QueryContainingQuote_DoesNotThrow()
    {
        using var idx = Migrate();
        idx.WriteChunks("doc1", filePath: null, pageCount: 1, [new("doc1", 0, 0, "she said \"hello there\" loudly")]);

        var hits = idx.SearchText("doc1", "\"hello there\"");

        Assert.Single(hits);
    }

    [Fact]
    public void SearchText_RespectsLimit_OnTrigramPath()
    {
        using var idx = Migrate();
        idx.WriteChunks("doc1", filePath: null, pageCount: 5,
            [.. Enumerable.Range(0, 5).Select(i => new PdfReaderApp.Models.Chunk(
                "doc1", i, i, "common phrase " + i.ToString(CultureInfo.InvariantCulture)))]);

        Assert.Equal(2, idx.SearchText("doc1", "common", limit: 2).Count);
    }

    [Fact]
    public void SearchText_RespectsLimit_OnLikeFallbackPath()
    {
        using var idx = Migrate();
        idx.WriteChunks("doc1", filePath: null, pageCount: 5,
            [.. Enumerable.Range(0, 5).Select(i => new PdfReaderApp.Models.Chunk(
                "doc1", i, i, "ab common " + i.ToString(CultureInfo.InvariantCulture)))]);

        Assert.Equal(3, idx.SearchText("doc1", "ab", limit: 3).Count);
    }

    [Fact]
    public void SearchText_ResultCarriesChunkId()
    {
        using var idx = Migrate();
        var ids = idx.WriteChunks("doc1", filePath: null, pageCount: 1, [new("doc1", 0, 0, "needle in haystack")]);

        var hit = Assert.Single(idx.SearchText("doc1", "needle"));

        Assert.Equal(ids[0], hit.ChunkId);
    }

    private void ExecuteRaw(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Dọn rác tạm: bỏ qua nếu file còn bị giữ.
        }
    }
}
