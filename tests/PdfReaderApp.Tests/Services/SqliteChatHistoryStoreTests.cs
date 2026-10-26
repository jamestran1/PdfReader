using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteChatHistoryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteChatHistoryStore _store;

    public SqliteChatHistoryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _store = new SqliteChatHistoryStore(Path.Combine(_dir, "chats.db"));
        _store.EnsureSchema();
    }

    [Fact]
    public void Append_Then_GetAll_ReturnsInChronologicalOrder()
    {
        _store.Append("doc1", "User", "Câu hỏi 1", 100);
        _store.Append("doc1", "AI", "Trả lời 1", 101);
        _store.Append("doc1", "User", "Câu hỏi 2", 102);

        var all = _store.GetAll("doc1");

        Assert.Equal(3, all.Count);
        Assert.Equal("User", all[0].Role);
        Assert.Equal("Câu hỏi 1", all[0].Content);
        Assert.Equal("AI", all[1].Role);
        Assert.Equal("Câu hỏi 2", all[2].Content);
    }

    [Fact]
    public void GetAll_IsolatesByDocumentId()
    {
        _store.Append("docA", "User", "thuộc A", 1);
        _store.Append("docB", "User", "thuộc B", 2);

        var a = _store.GetAll("docA");

        Assert.Single(a);
        Assert.Equal("thuộc A", a[0].Content);
    }

    [Fact]
    public void DeleteForDocument_RemovesOnlyThatDocument()
    {
        _store.Append("docA", "User", "a1", 1);
        _store.Append("docB", "User", "b1", 2);

        _store.DeleteForDocument("docA");

        Assert.Empty(_store.GetAll("docA"));
        Assert.Single(_store.GetAll("docB"));
    }

    [Fact]
    public void GetAll_UnknownDocument_ReturnsEmpty()
    {
        Assert.Empty(_store.GetAll("khong-ton-tai"));
    }

    [Fact]
    public void Append_WithSources_RoundTripsSourceTable()
    {
        RagSource[] sources = [new(1, "docA", "Hợp đồng", 2, "Điều 3: bên B thanh toán"), new(2, "docB", "Báo cáo \"Q3\"", 0)];

        _store.Append("doc1", "AI", "Theo [1] và [2]", 5, sources);

        Assert.Equal(sources, _store.GetAll("doc1").Single().Sources);
    }

    [Fact]
    public void Append_WithoutSources_ReturnsEmptySources()
    {
        _store.Append("doc1", "AI", "Trả lời", 5);

        Assert.Empty(_store.GetAll("doc1").Single().Sources);
    }

    [Fact]
    public void EnsureSchema_OldDatabaseWithoutSourcesColumn_MigratesAndKeepsOldRows()
    {
        string path = Path.Combine(_dir, "old-chats.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
CREATE TABLE chat_message (id INTEGER PRIMARY KEY AUTOINCREMENT, document_id TEXT NOT NULL, role TEXT NOT NULL, content TEXT NOT NULL, created_at INTEGER NOT NULL);
INSERT INTO chat_message (document_id, role, content, created_at) VALUES ('d', 'AI', 'Cũ [1]', 1);";
            cmd.ExecuteNonQuery();
        }
        var store = new SqliteChatHistoryStore(path);

        store.EnsureSchema();
        store.EnsureSchema();
        store.Append("d", "AI", "Mới [1]", 2, [new RagSource(1, "x", "T", 0)]);

        var all = store.GetAll("d");
        Assert.Empty(all[0].Sources);
        Assert.Single(all[1].Sources);
    }

    [Fact]
    public void GetAll_SourcesSavedBeforePassageExisted_LoadWithoutPassage()
    {
        string path = Path.Combine(_dir, "pre-passage.db");
        var store = new SqliteChatHistoryStore(path);
        store.EnsureSchema();
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
INSERT INTO chat_message (document_id, role, content, created_at, sources)
VALUES ('d', 'AI', 'Cũ [1]', 1, '[{""Number"":1,""DocumentId"":""x"",""Title"":""T"",""PageIndex"":3}]');";
            cmd.ExecuteNonQuery();
        }

        Assert.Equal([new RagSource(1, "x", "T", 3)], store.GetAll("d").Single().Sources);
    }

    public void Dispose()
    {
#pragma warning disable CA1031 // dọn thư mục tạm của test là best-effort
        try { Directory.Delete(_dir, true); } catch { }
#pragma warning restore CA1031
        GC.SuppressFinalize(this);
    }
}
