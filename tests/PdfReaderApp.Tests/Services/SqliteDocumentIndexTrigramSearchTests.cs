using System.Collections.Generic;
using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexTrigramSearchTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;
    private const string DocId = "viet-doc";

    public SqliteDocumentIndexTrigramSearchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(
            Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _idx.WriteChunks(DocId, null, 2, new List<Chunk>
        {
            new(DocId, 0, 0, "Tiếng Việt và Thiền định"),
            new(DocId, 1, 0, "Đường về quê hương")
        });
    }

    [Theory]
    [InlineData("viet")]
    [InlineData("Việt")]
    [InlineData("iet")]
    [InlineData("thien")]
    [InlineData("duong")]
    [InlineData("tieng viet")]
    public void SearchText_AccentInsensitive_ReturnsResults(string query)
    {
        var results = _idx.SearchText(DocId, query);
        Assert.NotEmpty(results);
    }

    [Fact]
    public void SearchText_NoMatch_ReturnsEmpty()
    {
        var results = _idx.SearchText(DocId, "xyzqwe");
        Assert.Empty(results);
    }

    [Fact]
    public void SearchText_Viet_HitsPage0()
    {
        var results = _idx.SearchText(DocId, "Việt");
        Assert.Contains(results, r => r.PageIndex == 0);
    }

    [Fact]
    public void SearchText_Duong_HitsPage1()
    {
        var results = _idx.SearchText(DocId, "duong");
        Assert.Contains(results, r => r.PageIndex == 1);
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
