using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;
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
        // Release SQLite connection pool file handles before deleting the temp dir (Windows WAL lock).
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
