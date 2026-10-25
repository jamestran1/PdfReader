using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

// Ghim hành vi lưu vector đa model: mỗi model một không gian vector độc lập, không đè lên nhau.
public sealed class SqliteDocumentIndexMultiModelTests : IDisposable
{
    private const string ModelA = "text-embedding-3-small";
    private const string ModelB = "gemini-embedding-001";
    private const string ModelC = "other-768";

    private readonly string _dir;
    private readonly string _dbPath;
    private readonly SqliteDocumentIndex _idx;
    private readonly IReadOnlyList<long> _ids;

    public SqliteDocumentIndexMultiModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "t.db");
        _idx = new SqliteDocumentIndex(_dbPath, Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        _idx.EnsureSchema();
        _ids = _idx.WriteChunks("doc1", null, 3, [
            new("doc1", 0, 0, "chunk zero"),
            new("doc1", 1, 1, "chunk one"),
            new("doc1", 2, 2, "chunk two")]);
    }

    private static float[] Hot(int dim, int hot)
    {
        var v = new float[dim];
        v[hot] = 1f;
        return v;
    }

    private void WriteHot(string model, int dim, int offset = 0) =>
        _idx.WriteEmbeddings(model, [.. _ids.Select((id, i) => (id, Hot(dim, (i + offset) % dim)))]);

    private string Scalar(string sql)
    {
        using var conn = new SqliteConnection($"Data Source={_dbPath};Pooling=False");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        return Convert.ToString(cmd.ExecuteScalar(), CultureInfo.InvariantCulture) ?? "";
    }

    [Fact]
    public void RetrieveRelevant_ModelsOfDifferentDimension_EachSeesOnlyItsOwnVectors()
    {
        WriteHot(ModelA, 1536);
        WriteHot(ModelB, 768, offset: 1); // chunk i có hot index i+1

        var a = _idx.RetrieveRelevant("doc1", ModelA, Hot(1536, 0), k: 3);
        var b = _idx.RetrieveRelevant("doc1", ModelB, Hot(768, 3), k: 3);

        Assert.Equal(3, a.Count);
        Assert.Equal("chunk zero", a[0].Text);
        Assert.Equal(3, b.Count);
        Assert.Equal("chunk two", b[0].Text);
    }

    [Fact]
    public void RetrieveRelevant_TwoModelsSameDimension_DoNotBleed()
    {
        // A: chunk zero nằm ở hot 0. B: chunk zero nằm ở hot 5, chunk one ở hot 0.
        _idx.WriteEmbeddings(ModelB, [(_ids[0], Hot(768, 5)), (_ids[1], Hot(768, 0))]);
        _idx.WriteEmbeddings(ModelC, [(_ids[0], Hot(768, 0)), (_ids[2], Hot(768, 7))]);

        var b = _idx.RetrieveRelevant("doc1", ModelB, Hot(768, 0), k: 5);
        var c = _idx.RetrieveRelevant("doc1", ModelC, Hot(768, 0), k: 5);

        Assert.Equal(2, b.Count);
        Assert.Equal("chunk one", b[0].Text);
        Assert.Equal(2, c.Count);
        Assert.Equal("chunk zero", c[0].Text);
    }

    [Fact]
    public void RetrieveRelevant_ModelWithoutVectors_ReturnsEmpty()
    {
        WriteHot(ModelA, 1536);

        Assert.Empty(_idx.RetrieveRelevant("doc1", ModelB, Hot(768, 0)));
        Assert.Empty(_idx.RetrieveRelevant("doc1", ModelC, Hot(1536, 0)));
    }

    [Fact]
    public void GetStatus_IsTrackedPerModel()
    {
        _idx.SetStatus("doc1", DocumentIndexStatus.Complete, ModelA);

        Assert.Equal(DocumentIndexStatus.Complete, _idx.GetStatus("doc1", ModelA));
        Assert.Equal(DocumentIndexStatus.None, _idx.GetStatus("doc1", ModelB));
    }

    [Fact]
    public void IndexingModelB_KeepsModelACompleteAndItsVectorsIntact()
    {
        WriteHot(ModelA, 1536);
        _idx.SetStatus("doc1", DocumentIndexStatus.Complete, ModelA);

        WriteHot(ModelB, 768);
        _idx.SetStatus("doc1", DocumentIndexStatus.Complete, ModelB);

        Assert.Equal(DocumentIndexStatus.Complete, _idx.GetStatus("doc1", ModelA));
        Assert.Equal(3, _idx.RetrieveRelevant("doc1", ModelA, Hot(1536, 0), k: 3).Count);
        Assert.Equal(3, _idx.RetrieveRelevant("doc1", ModelB, Hot(768, 0), k: 3).Count);
        Assert.Equal("3", Scalar("SELECT COUNT(*) FROM chunks"));
    }

    [Fact]
    public void DeleteEmbeddings_RemovesOnlyThatModelsVectors()
    {
        WriteHot(ModelB, 768);
        WriteHot(ModelC, 768);

        _idx.DeleteEmbeddings("doc1", ModelB);

        Assert.Empty(_idx.RetrieveRelevant("doc1", ModelB, Hot(768, 0)));
        Assert.Equal(3, _idx.RetrieveRelevant("doc1", ModelC, Hot(768, 0), k: 3).Count);
    }

    [Fact]
    public void GetChunks_ReturnsStoredChunksInOrderWithIds()
    {
        var chunks = _idx.GetChunks("doc1");

        Assert.Equal(_ids, chunks.Select(c => c.ChunkId));
        Assert.Equal(["chunk zero", "chunk one", "chunk two"], chunks.Select(c => c.Text), StringComparer.Ordinal);
        Assert.Empty(_idx.GetChunks("missing"));
    }

    [Fact]
    public void DeleteDocument_RemovesChunksModelRowsAndVectorsOfEveryModel()
    {
        WriteHot(ModelA, 1536);
        WriteHot(ModelB, 768);
        _idx.SetStatus("doc1", DocumentIndexStatus.Complete, ModelA);
        _idx.SetStatus("doc1", DocumentIndexStatus.Complete, ModelB);

        _idx.DeleteDocument("doc1");

        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM chunks"));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM document_models"));
        Assert.Equal(DocumentIndexStatus.None, _idx.GetStatus("doc1", ModelA));
        Assert.Empty(_idx.RetrieveRelevant("doc1", ModelA, Hot(1536, 0)));
        Assert.Empty(_idx.RetrieveRelevant("doc1", ModelB, Hot(768, 0)));
    }

    [Fact]
    public void WriteEmbeddings_SameChunkTwice_YieldsOneKnnHit()
    {
        _idx.WriteEmbeddings(ModelB, [(_ids[0], Hot(768, 0))]);
        _idx.WriteEmbeddings(ModelB, [(_ids[0], Hot(768, 0)), (_ids[1], Hot(768, 1))]);

        var hits = _idx.RetrieveRelevant("doc1", ModelB, Hot(768, 0), k: 10);

        Assert.Equal(2, hits.Count);
        Assert.Single(hits, h => h.Text == "chunk zero");
    }

    [Fact]
    public void WriteEmbeddings_SameChunkTwice_DoesNotTouchOtherModelsVectors()
    {
        _idx.WriteEmbeddings(ModelB, [(_ids[0], Hot(768, 0))]);
        _idx.WriteEmbeddings(ModelC, [(_ids[0], Hot(768, 0))]);

        _idx.WriteEmbeddings(ModelB, [(_ids[0], Hot(768, 0))]);

        Assert.Single(_idx.RetrieveRelevant("doc1", ModelC, Hot(768, 0), k: 10));
    }

    [Fact]
    public void WriteEmbeddings_MixedVectorLengths_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            _idx.WriteEmbeddings(ModelA, [(_ids[0], Hot(4, 0)), (_ids[1], Hot(8, 0))]));
    }

    [Fact]
    public void WriteEmbeddings_DoesNotCreateStatusRow()
    {
        _idx.WriteEmbeddings(ModelB, [(_ids[0], Hot(768, 0))]);

        Assert.Equal(DocumentIndexStatus.None, _idx.GetStatus("doc1", ModelB));
        Assert.Equal("0", Scalar("SELECT COUNT(*) FROM document_models"));
    }

    [Theory]
    [InlineData(DocumentIndexStatus.Indexing)]
    [InlineData(DocumentIndexStatus.Complete)]
    [InlineData(DocumentIndexStatus.Partial)]
    [InlineData(DocumentIndexStatus.TextOnly)]
    [InlineData(DocumentIndexStatus.Empty)]
    public void SetStatus_RoundTripsEveryStatus(DocumentIndexStatus status)
    {
        _idx.SetStatus("doc1", status, ModelA);

        Assert.Equal(status, _idx.GetStatus("doc1", ModelA));
    }

    [Fact]
    public void WriteEmbeddings_EmptyVector_Throws()
    {
        Assert.Throws<ArgumentException>(() => _idx.WriteEmbeddings(ModelA, [(_ids[0], [])]));
    }

    public void Dispose()
    {
        _idx.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // Dọn rác tạm: bỏ qua nếu file còn bị giữ.
        }

        GC.SuppressFinalize(this);
    }
}
