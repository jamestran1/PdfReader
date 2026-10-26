using System.IO;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

/// <summary>Quyền lập chỉ mục (document, model) là nguyên tử, và trạng thái Indexing mồ côi (app tắt giữa chừng) được dọn lúc mở.</summary>
public sealed class SqliteDocumentIndexClaimTests : IDisposable
{
    private const string Model = "m";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public SqliteDocumentIndexClaimTests() => Directory.CreateDirectory(_dir);

    private SqliteDocumentIndex Open()
    {
        var index = new SqliteDocumentIndex(Path.Combine(_dir, "t.db"), Path.Combine(AppContext.BaseDirectory, "vec0.dll"));
        index.EnsureSchema();
        return index;
    }

    [Fact]
    public void TryBeginIndexing_FirstCallWinsAndSecondLoses()
    {
        using var index = Open();

        Assert.True(index.TryBeginIndexing("doc1", Model, out _));
        Assert.False(index.TryBeginIndexing("doc1", Model, out _));
        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
    }

    [Fact]
    public void TryBeginIndexing_OtherModelOrDocument_IsIndependent()
    {
        using var index = Open();
        Assert.True(index.TryBeginIndexing("doc1", Model, out _));

        Assert.True(index.TryBeginIndexing("doc1", "other", out _));
        Assert.True(index.TryBeginIndexing("doc2", Model, out _));
    }

    [Fact]
    public void TryBeginIndexing_AfterClaimReleased_CanBeClaimedAgain()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out _);
        index.SetStatus("doc1", DocumentIndexStatus.Partial, Model);

        Assert.True(index.TryBeginIndexing("doc1", Model, out _));
    }

    [Fact]
    public async Task TryBeginIndexing_ConcurrentCallers_ExactlyOneWins()
    {
        using var index = Open();

        var results = await Task.WhenAll(Enumerable.Range(0, 16)
            .Select(attempt => Task.Run(() => index.TryBeginIndexing("doc1", Model, out long _))));

        Assert.Equal(1, results.Count(won => won));
    }

    [Fact]
    public void ReleaseIndexing_OwnClaim_SetsPartial()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long token);

        index.ReleaseIndexing("doc1", Model, token);

        Assert.Equal(DocumentIndexStatus.Partial, index.GetStatus("doc1", Model));
    }

    [Fact]
    public void ReleaseIndexing_StaleClaimAfterDeleteAndNewClaim_KeepsTheNewClaim()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long runA);
        index.DeleteDocument("doc1"); // "Lập chỉ mục lại": xoá rồi lượt B bắt đầu, lượt A chưa kịp nhận ra bị huỷ
        Assert.True(index.TryBeginIndexing("doc1", Model, out long runB));

        index.ReleaseIndexing("doc1", Model, runA);

        Assert.NotEqual(runA, runB);
        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
        Assert.False(index.TryBeginIndexing("doc1", Model, out _));
    }

    [Fact]
    public void ReleaseIndexing_StaleClaimAfterDeleteWithoutNewClaim_DoesNotCreateAStatusRow()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long runA);
        index.DeleteDocument("doc1");

        index.ReleaseIndexing("doc1", Model, runA);

        Assert.Equal(DocumentIndexStatus.None, index.GetStatus("doc1", Model));
    }

    private static float[] Hot(int dim)
    {
        var v = new float[1536];
        v[dim] = 1f;
        return v;
    }

    private static IReadOnlyList<long> SeedChunks(SqliteDocumentIndex index) =>
        index.WriteChunks("doc1", null, 1, [new Chunk("doc1", 0, 0, "text")]);

    [Fact]
    public void WriteEmbeddingsIfClaimed_CurrentClaim_WritesVectors()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long token);
        var ids = SeedChunks(index);

        Assert.True(index.WriteEmbeddingsIfClaimed("doc1", Model, token, [(ids[0], Hot(0))]));

        Assert.Single(index.RetrieveRelevant("doc1", Model, Hot(0)));
    }

    [Fact]
    public void WriteEmbeddingsIfClaimed_ClaimLostToReindex_WritesNothing()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long runA);
        index.DeleteDocument("doc1");
        index.TryBeginIndexing("doc1", Model, out _);
        var ids = SeedChunks(index);

        Assert.False(index.WriteEmbeddingsIfClaimed("doc1", Model, runA, [(ids[0], Hot(0))]));

        Assert.Empty(index.RetrieveRelevant("doc1", Model, Hot(0)));
    }

    [Fact]
    public void FinishIndexing_CurrentClaim_SetsStatus()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long token);

        Assert.True(index.FinishIndexing("doc1", Model, token, DocumentIndexStatus.Complete));

        Assert.Equal(DocumentIndexStatus.Complete, index.GetStatus("doc1", Model));
    }

    [Fact]
    public void FinishIndexing_ClaimLostToReindex_KeepsTheNewRunsIndexingStatus()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out long runA);
        index.DeleteDocument("doc1");
        index.TryBeginIndexing("doc1", Model, out _);

        Assert.False(index.FinishIndexing("doc1", Model, runA, DocumentIndexStatus.Complete));

        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
    }

    [Fact]
    public void WriteChunks_KeepsTheIndexingClaimOfTheWriter()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out _);

        index.WriteChunks("doc1", null, 1, [new Chunk("doc1", 0, 0, "text")]);

        // WriteChunks xoá mọi trạng thái của tài liệu; claim của lượt đang ghi phải sống sót để lượt thứ hai không chen vào.
        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
        Assert.False(index.TryBeginIndexing("doc1", Model, out _));
    }

    [Fact]
    public void EnsureSchema_OnOpen_ResetsOrphanedIndexingStatusToPartial()
    {
        using (var first = Open())
        {
            first.TryBeginIndexing("doc1", Model, out _);
            first.SetStatus("doc2", DocumentIndexStatus.Complete, Model);
        }

        using var reopened = Open();

        Assert.Equal(DocumentIndexStatus.Partial, reopened.GetStatus("doc1", Model));
        Assert.Equal(DocumentIndexStatus.Complete, reopened.GetStatus("doc2", Model));
    }

    [Fact]
    public void EnsureSchema_CalledAgainInSameSession_DoesNotResetLiveClaims()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model, out _);

        index.EnsureSchema();

        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            try { Directory.Delete(_dir, recursive: true); }
            catch (IOException) { /* best-effort: tệp tạm còn bị giữ thì để hệ điều hành dọn */ }
        }

        GC.SuppressFinalize(this);
    }
}
