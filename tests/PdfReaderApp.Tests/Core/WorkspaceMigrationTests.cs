using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Core;

public class WorkspaceMigrationTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteWorkspaceStore _wsStore;
    private readonly SqliteNoteStore _noteStore;

    public WorkspaceMigrationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _wsStore = new SqliteWorkspaceStore(Path.Combine(_dir, "workspaces.db"));
        _wsStore.EnsureSchema();
        _noteStore = new SqliteNoteStore(Path.Combine(_dir, "notes.db"));
        _noteStore.EnsureSchema();
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    [Fact]
    public void Run_CreatesDefaultWorkspaceForEachDocument_AndMovesNotes()
    {
        // Chuẩn bị: 2 tài liệu, mỗi tài liệu có ghi chú với owner_key = documentId
        _noteStore.Add(new Note("n1", "docA", "docA", 1, null, "ghi chú A1", 1, 1));
        _noteStore.Add(new Note("n2", "docA", "docA", 2, null, "ghi chú A2", 2, 2));
        _noteStore.Add(new Note("n3", "docB", "docB", 1, null, "ghi chú B1", 3, 3));

        var documents = new List<(string documentId, string title)>
        {
            ("docA", "Tài liệu A"),
            ("docB", "Tài liệu B"),
        };

        WorkspaceMigration.Run(_wsStore, _noteStore, documents, Now);

        // Mỗi tài liệu có đúng 1 default workspace
        var wsA = _wsStore.GetOrCreateDefaultForDocument("docA", "Tài liệu A", Now);
        var wsB = _wsStore.GetOrCreateDefaultForDocument("docB", "Tài liệu B", Now);
        Assert.NotEqual(wsA.Id, wsB.Id);

        // Ghi chú của docA đã chuyển sang wsA.Id
        Assert.Equal(2, _noteStore.GetForOwner(wsA.Id).Count);
        Assert.Empty(_noteStore.GetForOwner("docA"));

        // Ghi chú của docB đã chuyển sang wsB.Id
        Assert.Single(_noteStore.GetForOwner(wsB.Id));
        Assert.Empty(_noteStore.GetForOwner("docB"));
    }

    [Fact]
    public void Run_IsIdempotent_SecondRunCreatesNoDuplicateWorkspaceAndDoesNotDoubleMoveNotes()
    {
        _noteStore.Add(new Note("n1", "docA", "docA", 1, null, "ghi chú A", 1, 1));
        var documents = new List<(string documentId, string title)> { ("docA", "Tài liệu A") };
        long t = Now;

        WorkspaceMigration.Run(_wsStore, _noteStore, documents, t);
        WorkspaceMigration.Run(_wsStore, _noteStore, documents, t);

        // Vẫn chỉ 1 default workspace cho docA
        var all = _wsStore.GetAll(includeDefault: true);
        Assert.Single(all);

        // Ghi chú vẫn đúng 1 dòng dưới wsId
        var ws = _wsStore.GetOrCreateDefaultForDocument("docA", "Tài liệu A", t);
        Assert.Single(_noteStore.GetForOwner(ws.Id));
    }

    [Fact]
    public void Run_EmptyDocumentList_CreatesNoWorkspaces()
    {
        WorkspaceMigration.Run(_wsStore, _noteStore, new List<(string, string)>(), Now);
        Assert.Empty(_wsStore.GetAll(includeDefault: true));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
