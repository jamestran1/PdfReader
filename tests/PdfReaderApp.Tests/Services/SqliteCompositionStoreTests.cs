using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class SqliteCompositionStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteCompositionStore _store;

    public SqliteCompositionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _store = new SqliteCompositionStore(Path.Combine(_dir, "compositions.db"));
        _store.EnsureSchema();
    }

    private static Composition Make(string id, string owner = "ws", long createdAt = 1, IReadOnlyList<string>? sources = null)
        => new(id, owner, $"Tiêu đề {id}", CompositionKind.Summary, $"Nội dung {id} [1]", "gpt-test", sources ?? ["n1", "n2"], createdAt);

    [Fact]
    public void Add_ThenGetForOwner_RoundTripsEveryFieldIncludingSourceOrder()
    {
        var original = Make("c1", sources: ["z", "a", "m"]);
        _store.Add(original);

        var loaded = Assert.Single(_store.GetForOwner("ws"));

        Assert.Equal(original with { SourceNoteIds = [] }, loaded with { SourceNoteIds = [] });
        Assert.Equal(["z", "a", "m"], loaded.SourceNoteIds);
    }

    [Fact]
    public void GetForOwner_ReturnsNewestFirst_AndOnlyThatOwner()
    {
        _store.Add(Make("old", createdAt: 10));
        _store.Add(Make("new", createdAt: 30));
        _store.Add(Make("mid", createdAt: 20));
        _store.Add(Make("other", owner: "ws2", createdAt: 99));

        Assert.Equal(["new", "mid", "old"], [.. _store.GetForOwner("ws").Select(c => c.Id)]);
    }

    [Fact]
    public void DeleteForOwner_RemovesOnlyThatOwnersCompositions()
    {
        _store.Add(Make("a"));
        _store.Add(Make("b", owner: "ws2"));

        Assert.Equal(1, _store.DeleteForOwner("ws"));

        Assert.Empty(_store.GetForOwner("ws"));
        Assert.Single(_store.GetForOwner("ws2"));
    }

    [Fact]
    public void EnsureSchema_IsIdempotent_AndKeepsData()
    {
        _store.Add(Make("a"));

        _store.EnsureSchema();

        Assert.Single(_store.GetForOwner("ws"));
    }

    [Fact]
    public void Update_PersistsTitleContentAndEditTime_KeepingSourcesAndCreatedAt()
    {
        _store.Add(Make("c1", createdAt: 7, sources: ["z", "a"]));

        int rows = _store.Update("c1", "Ôn tập chương 3", "Nội dung mới [2]", 99);

        var loaded = Assert.Single(_store.GetForOwner("ws"));
        Assert.Equal(1, rows);
        Assert.Equal(("Ôn tập chương 3", "Nội dung mới [2]", 7L, (long?)99), (loaded.Title, loaded.Content, loaded.CreatedAtUnixMs, loaded.UpdatedAtUnixMs));
        Assert.Equal(["z", "a"], loaded.SourceNoteIds);
    }

    [Fact]
    public void Delete_RemovesOnlyThatComposition()
    {
        _store.Add(Make("keep"));
        _store.Add(Make("gone"));

        int rows = _store.Delete("gone");

        Assert.Equal(1, rows);
        Assert.Equal("keep", Assert.Single(_store.GetForOwner("ws")).Id);
    }

    [Fact]
    public void EnsureSchema_OnDatabaseCreatedBeforeEditTime_AddsColumnAndKeepsRows()
    {
        string path = Path.Combine(_dir, "old.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"CREATE TABLE composition (id TEXT PRIMARY KEY, owner_key TEXT NOT NULL, title TEXT NOT NULL, kind TEXT NOT NULL,
content TEXT NOT NULL, chat_model TEXT NOT NULL, source_note_ids TEXT NOT NULL, created_at INTEGER NOT NULL);
INSERT INTO composition VALUES ('o1','ws','Cũ','Summary','x','m','[]',5); PRAGMA user_version = 1;";
            cmd.ExecuteNonQuery();
        }

        var store = new SqliteCompositionStore(path);
        store.EnsureSchema();
        store.Update("o1", "Mới", "y", 8);

        var loaded = Assert.Single(store.GetForOwner("ws"));
        Assert.Equal(("Mới", "y", (long?)8), (loaded.Title, loaded.Content, loaded.UpdatedAtUnixMs));
    }

    public void Dispose()
    {
#pragma warning disable CA1031 // dọn thư mục tạm best-effort
        try { Directory.Delete(_dir, true); } catch (Exception) { /* bỏ qua */ }
#pragma warning restore CA1031
    }
}
