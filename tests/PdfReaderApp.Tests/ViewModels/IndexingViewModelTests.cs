using System.ClientModel;
using System.ClientModel.Primitives;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class IndexingViewModelTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

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

    private sealed class RecordingIndex : IDocumentIndex
    {
        public List<string> Deleted { get; } = [];
        public bool VectorSearchAvailable => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => DocumentIndexStatus.None;
        public void DeleteDocument(string documentId) => Deleted.Add(documentId);
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks)
            => Enumerable.Range(1, chunks.Count).Select(i => (long)i).ToList();
        public IReadOnlyList<StoredChunk> GetChunks(string documentId) => [];
        public void DeleteEmbeddings(string documentId, string embeddingModel) { }
        public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel) { }
        public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5) => [];
        public void Dispose() { }
    }

    private sealed class ThrowingEmbeddingFactory(Exception error) : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => throw error;
    }

    // Generator treo đến khi bị huỷ, để quan sát việc Dispose huỷ tác vụ nền.
    private sealed class BlockingEmbeddingFactory : IEmbeddingGeneratorFactory
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options) => new BlockingGenerator(this);
    }

    private sealed class BlockingGenerator(BlockingEmbeddingFactory owner) : IEmbeddingGenerator<string, Embedding<float>>
    {
        public async Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
            IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            owner.Started.TrySetResult();
            try { await Task.Delay(System.Threading.Timeout.Infinite, cancellationToken); }
            catch (OperationCanceledException)
            {
                owner.Cancelled.TrySetResult();
                throw;
            }
            return new GeneratedEmbeddings<Embedding<float>>([]);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeResponse(int status) : PipelineResponse
    {
        public override int Status => status;
        public override string ReasonPhrase => "fake";
        public override System.IO.Stream? ContentStream { get; set; }
        public override BinaryData Content => BinaryData.FromString(string.Empty);
        protected override PipelineResponseHeaders HeadersCore => throw new NotSupportedException();
        public override BinaryData BufferContent(CancellationToken cancellationToken = default) => Content;
        public override ValueTask<BinaryData> BufferContentAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(Content);
        public override void Dispose() { }
    }

    private static IndexingTarget Target(string documentId = "doc1")
        => new(documentId, "a.pdf", [new PageText(0, "nội dung trang một")]);

    private static IndexingViewModel Build(
        RecordingIndex index, IEmbeddingGeneratorFactory factory, Func<IndexingTarget?> target, Action<Action> runOnUi)
        => new(index, new DocumentIndexingService(index, factory, new FakeSettings()), target, runOnUi);

    // Chạy lượt lập chỉ mục tới lúc lỗi rồi xả hàng đợi UI, nên báo tiến độ tới muộn cũng được tính vào kết quả.
    private static async Task<string> RunToErrorAsync(Exception error)
    {
        var ui = new FakeUi();
        IndexingViewModel vm = Build(new RecordingIndex(), new ThrowingEmbeddingFactory(error), () => Target(), ui.RunOnUi);

        ui.Start(vm);
        await vm.BackgroundTask!.WaitAsync(Timeout);
        ui.Drain();

        return vm.StatusText;
    }

    [Fact]
    public async Task Start_NetworkError_ShowsSkippedMessage()
    {
        string status = await RunToErrorAsync(new System.Net.Http.HttpRequestException("down"));

        Assert.Equal("Lập chỉ mục AI bị bỏ qua: Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng. Tìm kiếm văn bản vẫn dùng được.", status);
    }

    // Giả luồng UI đơn luồng của WPF. Post (Progress<T>, ưu tiên Normal) chỉ xếp hàng; RunOnUi mô phỏng
    // Dispatcher.Invoke (ưu tiên Send) nên chạy trước mọi Post đang chờ. Test tự gọi Drain trên một luồng,
    // dựng đúng thứ tự "báo tiến độ tới sau lỗi" mà không phụ thuộc lịch luồng.
    private sealed class FakeUi : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _normal = new();
        private readonly Queue<Action> _urgent = new();

        public override void Post(SendOrPostCallback d, object? state)
        {
            lock (_normal) _normal.Enqueue((d, state));
        }

        public void RunOnUi(Action action)
        {
            lock (_normal) _urgent.Enqueue(action);
        }

        public void Drain()
        {
            while (true)
            {
                Action? urgent = null;
                (SendOrPostCallback Callback, object? State)? normal = null;
                lock (_normal)
                {
                    if (_urgent.Count > 0) urgent = _urgent.Dequeue();
                    else if (_normal.Count > 0) normal = _normal.Dequeue();
                    else return;
                }
                if (urgent is not null) urgent();
                else if (normal is { } next) next.Callback(next.State);
            }
        }

        // Progress<T> bắt SynchronizationContext lúc khởi tạo; chỉ đặt context giả quanh lệnh Start để phần
        // await của test không bị post vào hàng đợi không ai xả.
        public void Start(IndexingViewModel vm)
        {
            SynchronizationContext? original = Current;
            SetSynchronizationContext(this);
            try { vm.StartBackgroundIndexing(); }
            finally { SetSynchronizationContext(original); }
        }
    }

    // Lượt đầu dùng factory thứ nhất, lượt sau dùng factory thứ hai.
    private sealed class SequencedEmbeddingFactory(IEmbeddingGeneratorFactory first, IEmbeddingGeneratorFactory second)
        : IEmbeddingGeneratorFactory
    {
        private int _calls;

        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options)
            => (Interlocked.Increment(ref _calls) == 1 ? first : second).Create(options);
    }

    [Fact]
    public async Task Start_Error_LateProgressDoesNotOverwriteErrorMessage()
    {
        var error = new System.Net.Http.HttpRequestException("down");
        var ui = new FakeUi();
        IndexingViewModel vm = Build(new RecordingIndex(), new ThrowingEmbeddingFactory(error), () => Target(), ui.RunOnUi);

        ui.Start(vm);
        await vm.BackgroundTask!.WaitAsync(Timeout);
        ui.Drain(); // báo tiến độ "0/1" của chính lượt lỗi tới sau thông báo lỗi

        Assert.Equal(IndexingViewModel.FormatError(error), vm.StatusText);
    }

    [Fact]
    public async Task Start_DocumentSwitched_CancelledRunLateProgressDoesNotOverwriteNewRun()
    {
        var error = new System.Net.Http.HttpRequestException("down");
        var blocking = new BlockingEmbeddingFactory();
        var ui = new FakeUi();
        string documentId = "A";
        IndexingViewModel vm = Build(new RecordingIndex(),
            new SequencedEmbeddingFactory(blocking, new ThrowingEmbeddingFactory(error)),
            () => Target(documentId), ui.RunOnUi);

        ui.Start(vm); // tài liệu A: báo "0/1" rồi treo ở embed
        Task runA = vm.BackgroundTask!;
        await blocking.Started.Task.WaitAsync(Timeout);

        documentId = "B"; // đổi tài liệu: huỷ A, B lỗi ngay
        ui.Start(vm);
        await vm.BackgroundTask!.WaitAsync(Timeout);
        await runA.WaitAsync(Timeout);
        ui.Drain(); // báo tiến độ muộn của A

        Assert.Equal(IndexingViewModel.FormatError(error), vm.StatusText);
    }

    [Fact]
    public async Task Start_UnknownError_ShowsVietnameseMessageWithoutRawSdkText()
    {
        string status = await RunToErrorAsync(new InvalidOperationException("raw sdk detail"));

        Assert.DoesNotContain("raw sdk detail", status);
        Assert.Equal("Lập chỉ mục AI bị bỏ qua: Đã xảy ra lỗi không xác định khi gọi AI. Tìm kiếm văn bản vẫn dùng được.", status);
    }

    [Fact]
    public void FormatError_QuotaExhausted_ShowsQuotaMessage()
    {
        var error = new ClientResultException("insufficient_quota", new FakeResponse(429));

        Assert.Equal(
            "Lập chỉ mục AI bị bỏ qua: Tài khoản của nhà cung cấp AI không đủ credits. Hãy kiểm tra thanh toán ở trang của nhà cung cấp. Tìm kiếm văn bản vẫn dùng được.",
            IndexingViewModel.FormatError(error));
    }

    [Fact]
    public void FormatError_Unauthorized_ShowsInvalidKeyMessage()
    {
        var error = new ClientResultException("denied", new FakeResponse(401));

        Assert.Equal(
            "Lập chỉ mục AI bị bỏ qua: API key không hợp lệ, vui lòng kiểm tra lại trong Cài đặt. Tìm kiếm văn bản vẫn dùng được.",
            IndexingViewModel.FormatError(error));
    }

    [Theory]
    [InlineData(3, 10, "indexing", "Đang lập chỉ mục: 3/10")]
    [InlineData(10, 10, "complete", "")]
    public void FormatProgress_ReturnsCounterUntilComplete(int done, int total, string status, string expected)
    {
        Assert.Equal(expected, IndexingViewModel.FormatProgress(new IndexingProgress(done, total, status)));
    }

    [Fact]
    public void Reindex_WithTarget_DeletesOldIndexOfThatDocument()
    {
        var index = new RecordingIndex();
        using var vm = Build(index, new ThrowingEmbeddingFactory(new System.Net.Http.HttpRequestException()), () => Target("docX"), a => a());

        vm.ReindexDocumentCommand.Execute(parameter: null);

        Assert.Equal(["docX"], index.Deleted);
    }

    [Fact]
    public void Reindex_WithoutTarget_DoesNothing()
    {
        var index = new RecordingIndex();
        using var vm = Build(index, new ThrowingEmbeddingFactory(new System.Net.Http.HttpRequestException()), () => null, a => a());

        vm.ReindexDocumentCommand.Execute(parameter: null);

        Assert.Empty(index.Deleted);
        Assert.Equal(string.Empty, vm.StatusText);
    }

    [Fact]
    public async Task Dispose_CancelsRunningIndexing()
    {
        var factory = new BlockingEmbeddingFactory();
        var vm = Build(new RecordingIndex(), factory, () => Target(), a => a());
        vm.StartBackgroundIndexing();
        await factory.Started.Task.WaitAsync(Timeout);

        vm.Dispose();

        await factory.Cancelled.Task.WaitAsync(Timeout);
        Assert.DoesNotContain("lỗi", vm.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_ForNewDocument_CancelsPreviousIndexing()
    {
        var factory = new BlockingEmbeddingFactory();
        IndexingTarget? current = Target("docA");
        using var vm = Build(new RecordingIndex(), factory, () => current, a => a());
        vm.StartBackgroundIndexing();
        await factory.Started.Task.WaitAsync(Timeout);

        current = Target("docB");
        vm.StartBackgroundIndexing();

        await factory.Cancelled.Task.WaitAsync(Timeout);
    }
}
