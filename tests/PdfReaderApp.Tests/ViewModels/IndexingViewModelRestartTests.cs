using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

/// <summary>Lập chỉ mục lại ngay khi lượt cũ còn đang chờ lời gọi embedding (đổi key rồi lưu Settings): lượt mới phải
/// chờ lượt cũ nhả claim rồi mới chạy, và khi lượt khác giữ claim thì tab phải báo thay vì im lặng.</summary>
public sealed class IndexingViewModelRestartTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string Model = AiModelSettings.Default.EmbeddingModel;

    private sealed class FakeSettings : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => "key";
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => true;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    // Index trong bộ nhớ có đúng ngữ nghĩa claim của SqliteDocumentIndex: một token hiện hành, nhả/ghi/kết thúc chỉ khi còn token.
    private sealed class InMemoryIndex : IDocumentIndex
    {
        private readonly Lock _gate = new();
        private long _lastToken;
        private long _currentToken;
        private List<StoredChunk> _chunks = [];

        public DocumentIndexStatus Status { get; set; } = DocumentIndexStatus.None;
        public int VectorsWritten { get; private set; }

        public bool VectorSearchAvailable => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m) { lock (_gate) return Status; }
        public void DeleteDocument(string d) { lock (_gate) { Status = DocumentIndexStatus.None; _chunks = []; } }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c)
        {
            lock (_gate)
            {
                _chunks = [.. c.Select((chunk, i) => new StoredChunk(i + 1, chunk.Text))];
                return [.. _chunks.Select(s => s.ChunkId)];
            }
        }
        public IReadOnlyList<StoredChunk> GetChunks(string d) { lock (_gate) return _chunks; }
        public void DeleteEmbeddings(string d, string m) { }
        public void WriteEmbeddings(string m, IReadOnlyList<(long, float[])> e) { lock (_gate) VectorsWritten += e.Count; }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { lock (_gate) Status = s; }
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, string m, float[] v, int k = 5) => [];
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string d, string m, float[] v, int k = 5) => [];

        public bool TryBeginIndexing(string d, string m, out long claimToken)
        {
            lock (_gate)
            {
                claimToken = 0;
                if (Status == DocumentIndexStatus.Indexing) return false;
                Status = DocumentIndexStatus.Indexing;
                claimToken = _currentToken = ++_lastToken;
                return true;
            }
        }

        public void ReleaseIndexing(string d, string m, long claimToken)
        {
            lock (_gate)
            {
                if (claimToken == _currentToken && Status == DocumentIndexStatus.Indexing) Status = DocumentIndexStatus.Partial;
            }
        }

        public bool WriteEmbeddingsIfClaimed(string d, string m, long claimToken, IReadOnlyList<(long ChunkId, float[] Vector)> e)
        {
            lock (_gate)
            {
                if (claimToken != _currentToken || Status != DocumentIndexStatus.Indexing) return false;
                VectorsWritten += e.Count;
                return true;
            }
        }

        public bool FinishIndexing(string d, string m, long claimToken, DocumentIndexStatus status)
        {
            lock (_gate)
            {
                if (claimToken != _currentToken || Status != DocumentIndexStatus.Indexing) return false;
                Status = status;
                return true;
            }
        }

        public void Dispose() { }
    }

    // Lượt đầu: treo tới khi bị huỷ rồi còn chờ thêm cổng "release" mới ném (giả lập lời gọi HTTP trả về muộn).
    // Các lượt sau: trả vector ngay.
    private sealed class LateCancelThenFastFactory : IEmbeddingGeneratorFactory
    {
        private int _creations;

        public TaskCompletionSource FirstStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirst { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose)
            => Interlocked.Increment(ref _creations) == 1 ? new LateCancelGen(this) : new FastGen();

        private sealed class LateCancelGen(LateCancelThenFastFactory owner) : IEmbeddingGenerator<string, Embedding<float>>
        {
            public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
                CancellationToken cancellationToken = default)
            {
                owner.FirstStarted.TrySetResult();
                try { await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken); }
                catch (OperationCanceledException)
                {
                    await owner.ReleaseFirst.Task;
                    throw;
                }

                return new GeneratedEmbeddings<Embedding<float>>([]);
            }

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }

        private sealed class FastGen : IEmbeddingGenerator<string, Embedding<float>>
        {
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(_ => new Embedding<float>(new float[4])).ToList()));

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }

    private static IndexingTarget Target()
        => new("doc1", "a.pdf", [new PageText(0, "nội dung trang một")]);

    private static IndexingViewModel Build(InMemoryIndex index, IEmbeddingGeneratorFactory factory)
        => new(index, new DocumentIndexingService(index, factory, new FakeSettings()), () => Target(), runOnUiThread: action => action());

    [Fact]
    public async Task Start_WhilePreviousRunStillAwaitingEmbed_NewRunWaitsForItsReleaseAndCompletes()
    {
        var index = new InMemoryIndex();
        var factory = new LateCancelThenFastFactory();
        using var vm = Build(index, factory);

        vm.StartBackgroundIndexing();
        await factory.FirstStarted.Task.WaitAsync(Timeout);
        vm.StartBackgroundIndexing(); // OnSettingsSaved: huỷ lượt cũ và chạy ngay lượt mới, lượt cũ chưa kịp nhả claim
        factory.ReleaseFirst.SetResult(); // lời gọi HTTP của lượt cũ trả về muộn
        await vm.BackgroundTask!.WaitAsync(Timeout);

        Assert.Equal(DocumentIndexStatus.Complete, index.GetStatus("doc1", Model));
        Assert.True(index.VectorsWritten > 0);
    }

    [Fact]
    public async Task Start_AnotherRunHoldsTheClaim_ShowsBackgroundIndexingStatusInsteadOfSilence()
    {
        var index = new InMemoryIndex { Status = DocumentIndexStatus.Indexing }; // vd hàng đợi Workspace đang lập chỉ mục
        using var vm = Build(index, new LateCancelThenFastFactory());

        vm.StartBackgroundIndexing();
        await vm.BackgroundTask!.WaitAsync(Timeout);

        Assert.Equal(IndexingViewModel.IndexingElsewhereMessage, vm.StatusText);
        Assert.Equal("Tài liệu đang được lập chỉ mục ở nền, kết quả AI sẽ dùng được khi xong.", IndexingViewModel.IndexingElsewhereMessage);
    }
}
