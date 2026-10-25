using System.IO;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Bug #229: khung chat hiện provider đang dùng, thông báo thiếu key gọi đúng provider,
/// và lưu Settings thì lập chỉ mục tài liệu đang mở theo Embedding Model mới.</summary>
public sealed class MainViewModelAiProviderTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private readonly List<MainViewModel> _viewModels = [];
    private readonly List<string> _tempFiles = [];

    private sealed class MutableSettings(AiModelSettings initial, bool hasKey) : ISettingsService
    {
        public bool HasKey { get; set; } = hasKey;
        public AiModelSettings Models { get; set; } = initial;
        public string? GetApiKey(AiProvider provider) => HasKey ? "key" : null;
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => HasKey;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => Models;
        public void SaveModelSettings(AiModelSettings settings) => Models = settings;
    }

    private sealed class UnusedChatClientFactory : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => throw new InvalidOperationException("không dùng chat");
    }

    private sealed class FakeDocumentService : IPdfDocumentService
    {
        public void LoadFile(string filePath) { }
        public IReadOnlyList<TextBlock> ExtractStructure() => [];
        public IReadOnlyList<PageText> ExtractPageTexts() => [];
        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query) => [];
        public void Dispose() { }
    }

    private sealed class CountingEmbeddingFactory : IEmbeddingGeneratorFactory
    {
        public int Created;
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options)
        {
            Interlocked.Increment(ref Created);
            throw new InvalidOperationException("embedding không khả dụng");
        }
    }

    /// <summary>Ghi lại mỗi lần lập chỉ mục hỏi trạng thái theo model nào; model nằm trong <c>completeModels</c> thì báo Complete.</summary>
    private sealed class StatusProbeIndex(params string[] completeModels) : IDocumentIndex
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _probed = new();
        public bool VectorSearchAvailable => true;
        public bool Probed(string embeddingModel) => _probed.Contains(embeddingModel, StringComparer.Ordinal);
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel)
        {
            _probed.Enqueue(embeddingModel);
            return completeModels.Contains(embeddingModel) ? DocumentIndexStatus.Complete : DocumentIndexStatus.None;
        }
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks) => [];
        public int ChunkReads;
        public IReadOnlyList<StoredChunk> GetChunks(string documentId)
        {
            Interlocked.Increment(ref ChunkReads);
            return [];
        }
        public void DeleteEmbeddings(string documentId, string embeddingModel) { }
        public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel) { }
        public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5) => [];
        public void Dispose() { }
    }

    private MainViewModel BuildVm(
        MutableSettings settings, IDocumentIndex? index = null, IEmbeddingGeneratorFactory? embeddings = null)
    {
        var vm = new MainViewModel(
            new FakeDocumentService(),
            settings,
            new UnusedChatClientFactory(),
            index ?? new StatusProbeIndex(),
            embeddings ?? new CountingEmbeddingFactory(),
            workspaceStore: new FakeWorkspaceStore());
        _viewModels.Add(vm);
        return vm;
    }

    private void OpenFakeDocument(MainViewModel vm)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        File.WriteAllText(path, "nội dung giả " + Guid.NewGuid());
        _tempFiles.Add(path);
        vm.OpenLibraryItemCommand.Execute(
            new LibraryItem("docProv", "Sách provider", path, ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));
    }

    public void Dispose()
    {
        foreach (var vm in _viewModels) vm.Dispose();
        foreach (string path in _tempFiles)
        {
            try { File.Delete(path); }
            catch (IOException) { /* dọn dẹp best-effort */ }
            catch (UnauthorizedAccessException) { /* dọn dẹp best-effort */ }
        }
    }

    [Fact]
    public void ActiveModelLabel_ShowsChatProviderAndModel()
    {
        var settings = new MutableSettings(
            new AiModelSettings(AiProvider.Claude, "claude-sonnet-5-5", AiProvider.OpenAi, "text-embedding-3-small"), hasKey: true);

        var vm = BuildVm(settings);

        Assert.Equal("Anthropic Claude · claude-sonnet-5-5", vm.Chat.ActiveModelLabel);
    }

    [Fact]
    public void ActiveModelLabel_WithoutKey_AppendsMissingKeyHint()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: false);

        var vm = BuildVm(settings);

        Assert.Equal("OpenAI · gpt-4o-mini (chưa có key)", vm.Chat.ActiveModelLabel);
    }

    [Fact]
    public void OnSettingsSaved_RefreshesActiveModelLabel()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var vm = BuildVm(settings);
        Assert.Equal("OpenAI · gpt-4o-mini", vm.Chat.ActiveModelLabel);
        AiModelSettings before = settings.Models;

        settings.Models = new AiModelSettings(AiProvider.Gemini, "gemini-2.5-flash", AiProvider.Gemini, "gemini-embedding-001");
        vm.OnSettingsSaved(before, hadEmbeddingKey: true);

        Assert.Equal("Google Gemini · gemini-2.5-flash", vm.Chat.ActiveModelLabel);
    }

    [Fact]
    public async Task SendMessage_NotConfigured_NamesTheCurrentChatProvider()
    {
        var settings = new MutableSettings(
            new AiModelSettings(AiProvider.Claude, "claude-sonnet-5-5", AiProvider.OpenAi, "text-embedding-3-small"), hasKey: false);
        var vm = BuildVm(settings);
        vm.Chat.Input = "Xin chào";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal("Chưa có API key cho Anthropic Claude. Mở Cài đặt AI để nhập key.", vm.Chat.Messages[^1].Content);
    }

    private static readonly AiModelSettings GeminiEmbedding =
        new(AiProvider.OpenAi, "gpt-4o-mini", AiProvider.Gemini, "gemini-embedding-001");

    [Fact]
    public async Task OnSettingsSaved_EmbeddingModelChanged_IndexesWithNewEmbeddingModel()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var index = new StatusProbeIndex();
        var vm = BuildVm(settings, index);
        OpenFakeDocument(vm);
        await vm.Indexing.BackgroundTask!.WaitAsync(Timeout);
        AiModelSettings before = settings.Models;

        settings.Models = GeminiEmbedding;
        vm.OnSettingsSaved(before, hadEmbeddingKey: true);
        await vm.Indexing.BackgroundTask!.WaitAsync(Timeout);

        Assert.True(index.Probed("gemini-embedding-001"));
    }

    [Fact]
    public async Task OnSettingsSaved_EmbeddingUnchanged_DoesNotRestartIndexing()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var vm = BuildVm(settings, new StatusProbeIndex());
        OpenFakeDocument(vm);
        Task running = vm.Indexing.BackgroundTask!;
        await running.WaitAsync(Timeout);
        AiModelSettings before = settings.Models;

        // Chỉ đổi chat model: embedding giữ nguyên.
        settings.Models = before with { ChatModel = "gpt-4o" };
        vm.OnSettingsSaved(before, hadEmbeddingKey: true);

        Assert.Same(running, vm.Indexing.BackgroundTask);
    }

    [Fact]
    public async Task OnSettingsSaved_EmbeddingKeyAdded_StartsIndexing()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: false);
        var vm = BuildVm(settings, new StatusProbeIndex());
        OpenFakeDocument(vm);
        Task first = vm.Indexing.BackgroundTask!;
        await first.WaitAsync(Timeout);
        AiModelSettings before = settings.Models;

        settings.HasKey = true; // người dùng vừa nhập key, embedding giữ nguyên
        vm.OnSettingsSaved(before, hadEmbeddingKey: false);

        Assert.NotSame(first, vm.Indexing.BackgroundTask);
        await vm.Indexing.BackgroundTask!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task OnSettingsSaved_ModelAlreadyComplete_DoesNotReadChunksOrCallEmbeddingApi()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var index = new StatusProbeIndex("gemini-embedding-001");
        var embeddings = new CountingEmbeddingFactory();
        var vm = BuildVm(settings, index, embeddings);
        OpenFakeDocument(vm);
        await vm.Indexing.BackgroundTask!.WaitAsync(Timeout);
        int chunkReadsBefore = index.ChunkReads;
        AiModelSettings before = settings.Models;

        settings.Models = GeminiEmbedding;
        vm.OnSettingsSaved(before, hadEmbeddingKey: true);
        await vm.Indexing.BackgroundTask!.WaitAsync(Timeout);

        Assert.True(index.Probed("gemini-embedding-001"));
        Assert.Equal(chunkReadsBefore, index.ChunkReads); // Complete: thoát trước khi đụng chunk
        Assert.Equal(0, embeddings.Created);
    }

    [Fact]
    public void OnSettingsSaved_WithoutDocument_DoesNotThrowOrIndex()
    {
        var index = new StatusProbeIndex();
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var vm = BuildVm(settings, index);
        AiModelSettings before = settings.Models;

        settings.Models = GeminiEmbedding;
        vm.OnSettingsSaved(before, hadEmbeddingKey: true);

        Assert.False(index.Probed("gemini-embedding-001"));
    }
}
