using System;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteWorkspaceStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteWorkspaceStore _store;

    public SqliteWorkspaceStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _store = new SqliteWorkspaceStore(Path.Combine(_dir, "workspaces.db"));
        _store.EnsureSchema();
    }

    [Fact]
    public void Upsert_Then_Get_ReturnsWorkspace()
    {
        _store.Upsert(new Workspace("w1", "Dự án A", false, null, 100, 100));
        var got = _store.Get("w1");
        Assert.NotNull(got);
        Assert.Equal("Dự án A", got!.Name);
        Assert.False(got.IsDefault);
    }

    [Fact]
    public void GetAll_ExcludesDefaultUnlessRequested()
    {
        _store.Upsert(new Workspace("user1", "Dự án người dùng", false, null, 1, 1));
        _store.Upsert(new Workspace("def1", "Tài liệu lẻ", true, "docX", 2, 2));

        var visible = _store.GetAll(includeDefault: false);
        Assert.Single(visible);
        Assert.Equal("user1", visible[0].Id);

        var all = _store.GetAll(includeDefault: true);
        Assert.Equal(2, all.Count);
    }

    [Fact]
    public void Membership_AddRemoveAndQuery()
    {
        _store.Upsert(new Workspace("w1", "WS", false, null, 1, 1));
        _store.AddDocument("w1", "docA");
        _store.AddDocument("w1", "docB");

        Assert.Equal(2, _store.GetDocumentIds("w1").Count);
        Assert.Contains("w1", _store.GetWorkspaceIdsForDocument("docA"));

        _store.RemoveDocument("w1", "docA");
        var ids = _store.GetDocumentIds("w1");
        Assert.Single(ids);
        Assert.Equal("docB", ids[0]);
    }

    [Fact]
    public void GetOrCreateDefaultForDocument_CreatesOnce()
    {
        var w = _store.GetOrCreateDefaultForDocument("docX", "Tài liệu X", 5);
        Assert.True(w.IsDefault);
        Assert.Equal("docX", w.DefaultDocumentId);
        Assert.Contains("docX", _store.GetDocumentIds(w.Id));

        var w2 = _store.GetOrCreateDefaultForDocument("docX", "Tài liệu X", 9);
        Assert.Equal(w.Id, w2.Id);
        Assert.Single(_store.GetAll(includeDefault: true));
    }

    [Fact]
    public void Rename_And_Delete()
    {
        _store.Upsert(new Workspace("w1", "Cũ", false, null, 1, 1));
        _store.AddDocument("w1", "d1");

        _store.Rename("w1", "Mới", 5);
        Assert.Equal("Mới", _store.Get("w1")!.Name);

        _store.Delete("w1");
        Assert.Null(_store.Get("w1"));
        Assert.Empty(_store.GetDocumentIds("w1"));
    }

    [Fact]
    public void Delete_RemovesWorkspaceAndMembership()
    {
        // Upsert ws "w1"; AddDocument("w1","docA")
        _store.Upsert(new Workspace("w1", "Dự án test", false, null, 1, 1));
        _store.AddDocument("w1", "docA");

        _store.Delete("w1");

        // workspace w1 phải không còn
        Assert.Null(_store.Get("w1"));
        // membership phải sạch
        Assert.Empty(_store.GetDocumentIds("w1"));
        // GetWorkspaceIdsForDocument("docA") không chứa "w1"
        Assert.DoesNotContain("w1", _store.GetWorkspaceIdsForDocument("docA"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
