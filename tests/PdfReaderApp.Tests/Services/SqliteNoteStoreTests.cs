using System;
using System.Collections.Generic;
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
        new(id, owner, owner, page, null, content, t, t);

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
    public void Add_WithQuote_RoundTrips()
    {
        _store.Add(new Note("q1", "docA", "docA", 2, "đoạn trích", "bình luận", 10, 10));
        var got = _store.GetForOwner("docA").Single();
        Assert.Equal("đoạn trích", got.Quote);
        Assert.Equal("bình luận", got.Content);
    }

    [Fact]
    public void EnsureSchema_MigratesV1DbByAddingQuoteColumn()
    {
        string db = Path.Combine(_dir, "v1.db");
        // Tạo bảng kiểu v1 (KHÔNG có cột quote), user_version chưa set, có 1 dòng.
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var c = conn.CreateCommand();
            c.CommandText = @"CREATE TABLE note (id TEXT PRIMARY KEY, owner_key TEXT NOT NULL, document_id TEXT,
                page_index INTEGER, content TEXT NOT NULL, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
                INSERT INTO note (id, owner_key, document_id, page_index, content, created_at, updated_at)
                VALUES ('old','docA','docA',1,'cũ',1,1);";
            c.ExecuteNonQuery();
        }

        var store = new SqliteNoteStore(db);
        store.EnsureSchema(); // phải thêm cột quote, set user_version=2, giữ dữ liệu cũ

        var got = store.GetForOwner("docA").Single();
        Assert.Equal("cũ", got.Content);
        Assert.Null(got.Quote);

        // ghi note có quote vào db đã migrate -> đọc lại được
        store.Add(new Note("new", "docA", "docA", 1, "trích", "mới", 2, 2));
        Assert.Contains(store.GetForOwner("docA"), n => n.Quote == "trích");
    }

    [Fact]
    public void NullPageAndDocument_RoundTrip()
    {
        _store.Add(new Note("a", "docA", null, null, null, "tự do", 1, 1));
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

    [Fact]
    public void Add_WithRectsAndColor_RoundTrips()
    {
        var rects = new List<HighlightRect> { new(1, 2, 30, 10), new(1, 14, 25, 10) };
        _store.Add(new Note("h1", "docA", "docA", 3, "trích", "ghi chú", 10, 10, rects, "#FFEB3B"));
        var got = _store.GetForOwner("docA").Single();
        Assert.NotNull(got.Rects);
        Assert.Equal(2, got.Rects!.Count);
        Assert.Equal(30, got.Rects[0].W);
        Assert.Equal("#FFEB3B", got.Color);
    }

    [Fact]
    public void Add_NullRects_RoundTripsAsNull()
    {
        _store.Add(new Note("n1", "docA", "docA", 1, null, "tự do", 1, 1));
        var got = _store.GetForOwner("docA").Single();
        Assert.Null(got.Rects);
        Assert.Null(got.Color);
    }

    [Fact]
    public void EnsureSchema_MigratesV2DbByAddingRectsAndColor()
    {
        string db = Path.Combine(_dir, "v2.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
        {
            conn.Open();
            using var c = conn.CreateCommand();
            // bảng kiểu v2 (có quote, CHƯA có rects/color), user_version=2, 1 dòng
            c.CommandText = @"CREATE TABLE note (id TEXT PRIMARY KEY, owner_key TEXT NOT NULL, document_id TEXT,
                page_index INTEGER, quote TEXT, content TEXT NOT NULL, created_at INTEGER NOT NULL, updated_at INTEGER NOT NULL);
                PRAGMA user_version = 2;
                INSERT INTO note (id, owner_key, document_id, page_index, quote, content, created_at, updated_at)
                VALUES ('old','docA','docA',1,'q','cũ',1,1);";
            c.ExecuteNonQuery();
        }

        var store = new SqliteNoteStore(db);
        store.EnsureSchema();

        var got = store.GetForOwner("docA").Single();
        Assert.Equal("cũ", got.Content);
        Assert.Null(got.Rects);
        store.Add(new Note("new", "docA", "docA", 1, "q2", "mới", 2, 2,
            new List<HighlightRect> { new(0, 0, 5, 5) }, "#FFEB3B"));
        Assert.Contains(store.GetForOwner("docA"), n => n.Rects != null && n.Rects.Count == 1);
    }

    [Fact]
    public void ReassignOwner_MovesNotesToNewKey()
    {
        _store.Add(N("a", "docOld", 1, "ghi chú 1", 1));
        _store.Add(N("b", "docOld", 2, "ghi chú 2", 2));
        _store.Add(N("c", "docOther", 1, "không đổi", 3));

        int moved = _store.ReassignOwner("docOld", "wsNew");

        Assert.Equal(2, moved);
        Assert.Empty(_store.GetForOwner("docOld"));
        Assert.Equal(2, _store.GetForOwner("wsNew").Count);
        // note của owner khác không bị ảnh hưởng
        Assert.Single(_store.GetForOwner("docOther"));
    }

    [Fact]
    public void ReassignOwner_IsIdempotent_SecondCallMovesNothing()
    {
        _store.Add(N("a", "docOld", 1, "ghi chú", 1));
        _store.ReassignOwner("docOld", "wsNew");

        // Gọi lần hai: không còn dòng nào với docOld -> trả về 0, wsNew vẫn còn 1 dòng
        int second = _store.ReassignOwner("docOld", "wsNew");

        Assert.Equal(0, second);
        Assert.Single(_store.GetForOwner("wsNew"));
    }

    [Fact]
    public void DeleteForOwner_RemovesOnlyThatOwnersNotes()
    {
        // Thêm note owner=W1 và owner=W2
        _store.Add(N("a", "W1", 1, "ghi chú W1", 1));
        _store.Add(N("b", "W1", 2, "ghi chú W1 số 2", 2));
        _store.Add(N("c", "W2", 1, "ghi chú W2", 3));

        int deleted = _store.DeleteForOwner("W1");

        // Chỉ xóa note của W1
        Assert.Equal(2, deleted);
        Assert.Empty(_store.GetForOwner("W1"));
        // Note của W2 vẫn còn
        Assert.Single(_store.GetForOwner("W2"));
        Assert.Equal("ghi chú W2", _store.GetForOwner("W2")[0].Content);
    }

    [Fact]
    public void DeleteForDocument_RemovesOnlyNotesAnchoredToThatDocument()
    {
        // note doc=docA (owner W) và doc=docB
        _store.Add(new Note("a", "W", "docA", 1, null, "neo docA", 1, 1));
        _store.Add(new Note("b", "W", "docA", 2, null, "neo docA số 2", 2, 2));
        _store.Add(new Note("c", "W", "docB", 1, null, "neo docB", 3, 3));

        int deleted = _store.DeleteForDocument("docA");

        // Chỉ xóa note neo tới docA
        Assert.Equal(2, deleted);
        // Note neo tới docB vẫn còn
        var remaining = _store.GetForOwner("W");
        Assert.Single(remaining);
        Assert.Equal("neo docB", remaining[0].Content);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
