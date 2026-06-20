using System.Collections.Generic;
using System.IO;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class SqliteDocumentIndexKnnTests : IDisposable
{
    private readonly string _dir;
    private readonly SqliteDocumentIndex _idx;

    private static float[] Hot(int dim)
    {
        var v = new float[1536];
        v[dim] = 1f;
        return v;
    }

    public SqliteDocumentIndexKnnTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _idx = new SqliteDocumentIndex(Path.Combine(_dir, "t.db"),
            Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        var ids = _idx.WriteChunks("doc1", null, 3, new List<Chunk>
        {
            new("doc1", 0, 0, "chunk hot-0"),
            new("doc1", 1, 1, "chunk hot-1"),
            new("doc1", 2, 2, "chunk hot-2")
        });
        _idx.WriteEmbeddings(new List<(long, float[])>
        {
            (ids[0], Hot(0)), (ids[1], Hot(1)), (ids[2], Hot(2))
        });
    }

    [Fact]
    public void RetrieveRelevant_ReturnsNearestFirst()
    {
        // query closest to dimension 1 -> chunk "hot-1" should rank first
        var results = _idx.RetrieveRelevant("doc1", Hot(1), k: 3);

        Assert.NotEmpty(results);
        Assert.Contains("hot-1", results[0].Text);
    }

    [Fact]
    public void RetrieveRelevant_RespectsK()
    {
        var results = _idx.RetrieveRelevant("doc1", Hot(0), k: 2);
        Assert.InRange(results.Count, 1, 2);
    }

    public void Dispose()
    {
        _idx.Dispose();
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }
}
