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
