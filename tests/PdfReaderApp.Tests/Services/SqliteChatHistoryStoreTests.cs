using System;
using System.IO;
using System.Linq;
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

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }
}
