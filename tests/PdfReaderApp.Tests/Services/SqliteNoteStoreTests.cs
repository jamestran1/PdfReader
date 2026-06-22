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
EOF 2>&1
