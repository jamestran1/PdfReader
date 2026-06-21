using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexSnippetTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;
    private const string DocId = "snippet-doc";

    public SqliteDocumentIndexSnippetTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(
            Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _idx.WriteChunks(DocId, null, 1, new List<Chunk>
        {
            new(DocId, 0, 0, "Hợp đồng bảo hiểm có hiệu lực từ ngày ký kết")
        });
    }

    [Fact]
    public void SearchText_Snippet_PreservesDiacritics()
    {
        var results = _idx.SearchText(DocId, "bao hiem");
        var hit = results.First(r => r.PageIndex == 0);
        Assert.Contains("bảo hiểm", hit.Snippet);
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
