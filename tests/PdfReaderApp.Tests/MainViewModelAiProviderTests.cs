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
        public AiModelSettings Models { get; set; } = initial;
        public string? GetApiKey(AiProvider provider) => hasKey ? "key" : null;
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => hasKey;
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
        public IReadOnlyList<StoredChunk> GetChunks(string documentId) => [];
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

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Điều kiện không đạt trong thời hạn.");
            await Task.Delay(20);
        }
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
    public void OnSettingsSaved_RefreshesActiveModelLabel()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var vm = BuildVm(settings);
        Assert.Equal("OpenAI · gpt-4o-mini", vm.Chat.ActiveModelLabel);

        settings.Models = new AiModelSettings(AiProvider.Gemini, "gemini-2.5-flash", AiProvider.Gemini, "gemini-embedding-001");
        vm.OnSettingsSaved();

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

    [Fact]
    public async Task OnSettingsSaved_WithOpenDocument_IndexesWithNewEmbeddingModel()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var index = new StatusProbeIndex();
        var vm = BuildVm(settings, index);
        OpenFakeDocument(vm);
        await WaitUntilAsync(() => index.Probed("text-embedding-3-small"));

        settings.Models = new AiModelSettings(AiProvider.OpenAi, "gpt-4o-mini", AiProvider.Gemini, "gemini-embedding-001");
        vm.OnSettingsSaved();

        await WaitUntilAsync(() => index.Probed("gemini-embedding-001"));
    }

    [Fact]
    public async Task OnSettingsSaved_ModelAlreadyComplete_DoesNotCallEmbeddingApi()
    {
        var settings = new MutableSettings(AiModelSettings.Default, hasKey: true);
        var index = new StatusProbeIndex("gemini-embedding-001");
        var embeddings = new CountingEmbeddingFactory();
        var vm = BuildVm(settings, index, embeddings);
        OpenFakeDocument(vm);
        await WaitUntilAsync(() => index.Probed("text-embedding-3-small"));
        int createdBefore = embeddings.Created;

        settings.Models = new AiModelSettings(AiProvider.OpenAi, "gpt-4o-mini", AiProvider.Gemini, "gemini-embedding-001");
        vm.OnSettingsSaved();
        await WaitUntilAsync(() => index.Probed("gemini-embedding-001"));
        await Task.Delay(100);

        Assert.Equal(createdBefore, embeddings.Created);
    }

    [Fact]
    public void OnSettingsSaved_WithoutDocument_DoesNotThrowOrIndex()
    {
        var index = new StatusProbeIndex();
        var vm = BuildVm(new MutableSettings(AiModelSettings.Default, hasKey: true), index);

        vm.OnSettingsSaved();

        Assert.False(index.Probed("text-embedding-3-small"));
    }
}
