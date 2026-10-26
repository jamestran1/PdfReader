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

        Assert.True(index.TryBeginIndexing("doc1", Model));
        Assert.False(index.TryBeginIndexing("doc1", Model));
        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
    }

    [Fact]
    public void TryBeginIndexing_OtherModelOrDocument_IsIndependent()
    {
        using var index = Open();
        Assert.True(index.TryBeginIndexing("doc1", Model));

        Assert.True(index.TryBeginIndexing("doc1", "other"));
        Assert.True(index.TryBeginIndexing("doc2", Model));
    }

    [Fact]
    public void TryBeginIndexing_AfterClaimReleased_CanBeClaimedAgain()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model);
        index.SetStatus("doc1", DocumentIndexStatus.Partial, Model);

        Assert.True(index.TryBeginIndexing("doc1", Model));
    }

    [Fact]
    public void TryBeginIndexing_ConcurrentCallers_ExactlyOneWins()
    {
        using var index = Open();

        var results = Enumerable.Range(0, 16)
            .Select(_ => Task.Run(() => index.TryBeginIndexing("doc1", Model)))
            .ToArray();
        Task.WaitAll(results);

        Assert.Equal(1, results.Count(r => r.Result));
    }

    [Fact]
    public void WriteChunks_KeepsTheIndexingClaimOfTheWriter()
    {
        using var index = Open();
        index.TryBeginIndexing("doc1", Model);

        index.WriteChunks("doc1", null, 1, [new Chunk("doc1", 0, 0, "text")]);

        // WriteChunks xoá mọi trạng thái của tài liệu; claim của lượt đang ghi phải sống sót để lượt thứ hai không chen vào.
        Assert.Equal(DocumentIndexStatus.Indexing, index.GetStatus("doc1", Model));
        Assert.False(index.TryBeginIndexing("doc1", Model));
    }

    [Fact]
    public void EnsureSchema_OnOpen_ResetsOrphanedIndexingStatusToPartial()
    {
        using (var first = Open())
        {
            first.TryBeginIndexing("doc1", Model);
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
        index.TryBeginIndexing("doc1", Model);

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
