using System;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
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

    [Fact]
    public void Upsert_Then_Get_RoundTripsAuthorAndPublisher()
    {
        var stored = new LibraryItem("doc1", "doc1.pdf", "/lib/doc1.pdf", "/thumb/doc1.png",
            10, 100, 500, Author: "Nguyễn Văn A", Publisher: "NXB Trẻ");
        _store.Upsert(stored);

        var loaded = _store.Get("doc1");

        Assert.NotNull(loaded);
        Assert.Equal("Nguyễn Văn A", loaded!.Author);
        Assert.Equal("NXB Trẻ", loaded.Publisher);
    }

    [Fact]
    public void Get_WhenAuthorAndPublisherNull_ReturnsNulls()
    {
        _store.Upsert(new LibraryItem("doc2", "doc2.pdf", "/lib/doc2.pdf", null, 3, 1, 1));

        var loaded = _store.Get("doc2");

        Assert.NotNull(loaded);
        Assert.Null(loaded!.Author);
        Assert.Null(loaded.Publisher);
    }

    [Fact]
    public void EnsureSchema_OnLegacyDbWithoutMetadataColumns_AddsColumnsAndPreservesRows()
    {
        string dbPath = Path.Combine(_dir, "legacy.db");
        using (var conn = new SqliteConnection($"Data Source={dbPath};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE library (
  document_id TEXT PRIMARY KEY, title TEXT NOT NULL, stored_path TEXT NOT NULL,
  thumb_path TEXT, page_count INTEGER NOT NULL, imported_at INTEGER NOT NULL, last_opened_at INTEGER NOT NULL);
INSERT INTO library VALUES ('old', 'old.pdf', '/lib/old.pdf', NULL, 5, 10, 20);";
            cmd.ExecuteNonQuery();
        }

        var migrated = new SqliteLibraryStore(dbPath);
        migrated.EnsureSchema();

        var loaded = migrated.Get("old");
        Assert.NotNull(loaded);
        Assert.Equal("old.pdf", loaded!.Title);
        Assert.Null(loaded.Author);
        migrated.Upsert(loaded with { Author = "Tác giả" });
        Assert.Equal("Tác giả", migrated.Get("old")!.Author);
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
