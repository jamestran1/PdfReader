using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexSearchTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;

    public SqliteDocumentIndexSearchTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _idx.WriteChunks("doc1", null, 3, new List<Chunk>
        {
            new("doc1", 0, 0, "the quick brown fox"),
            new("doc1", 1, 1, "lazy dog sleeps"),
            new("doc1", 2, 2, "the fox jumps high")
        });
        _idx.WriteChunks("doc2", null, 1, new List<Chunk>
        {
            new("doc2", 0, 0, "unrelated fox content")
        });
    }

    [Fact]
    public void SearchText_FindsMatchingChunks_WithPageIndex()
    {
        var results = _idx.SearchText("doc1", "fox");

        Assert.Equal(2, results.Count);
        Assert.All(results, r => Assert.Contains(r.PageIndex, new[] { 0, 2 }));
    }

    [Fact]
    public void SearchText_FiltersByDocument()
    {
        var results = _idx.SearchText("doc1", "fox");
        // doc2's "unrelated fox content" must not appear
        Assert.DoesNotContain(results, r => r.Snippet.Contains("unrelated"));
    }

    [Fact]
    public void SearchText_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(_idx.SearchText("doc1", "elephant"));
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
