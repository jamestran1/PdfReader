using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Ghim hành vi của SendMessageCommand và của constructor MainViewModel trước khi tách method.</summary>
public sealed class MainViewModelChatTests : IDisposable
{
    private readonly List<MainViewModel> _viewModels = [];
    private readonly List<string> _tempDbPaths = [];
    private readonly List<string> _tempFiles = [];

    private const string NotConfigured =
        "Chưa có API key cho OpenAI. Mở Cài đặt AI để nhập key.";

    private sealed class FakeSettings(bool hasKey) : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => hasKey ? "key" : null;
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => hasKey;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    private sealed class FakeChatClientFactory(Func<IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => new FakeChatClient(stream);
    }

    private sealed class FakeChatClient(Func<IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<AiMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AiMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => stream();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeChatHistoryStore : IChatHistoryStore
    {
        public readonly List<(string owner, string role, string content)> Appended = [];
        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix, IReadOnlyList<PdfReaderApp.Models.RagSource>? sources = null)
            => Appended.Add((documentId, role, content));
        public List<ChatHistoryEntry> Stored { get; } = [];
        public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId) => Stored.ToList();
        public void DeleteForDocument(string documentId) { }
    }

    private sealed class FakeDocumentService : IPdfDocumentService
    {
        public void LoadFile(string filePath) { }
        public IReadOnlyList<TextBlock> ExtractStructure() => [];
        public IReadOnlyList<PageText> ExtractPageTexts() => [];
        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query) => [];
        public IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments) => [];
        public void Dispose() { }
    }

    private sealed class FakeDocumentIndex(DocumentIndexStatus status) : IDocumentIndex
    {
        public bool VectorSearchAvailable => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => status;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks) => [];
        public IReadOnlyList<StoredChunk> GetChunks(string documentId) => [];
        public void DeleteEmbeddings(string documentId, string embeddingModel) { }
        public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel) { }
        public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string documentId, string embeddingModel, float[] queryVector, int k = 5) => [];
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string documentId, string embeddingModel, float[] queryVector, int k = 5) => [];
        public bool TryBeginIndexing(string documentId, string embeddingModel, out long claimToken)
        {
            claimToken = 0;
            return true;
        }
        public void ReleaseIndexing(string documentId, string embeddingModel, long claimToken) { }
        public bool WriteEmbeddingsIfClaimed(string documentId, string embeddingModel, long claimToken, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) => true;
        public bool FinishIndexing(string documentId, string embeddingModel, long claimToken, DocumentIndexStatus status) => true;
        public void Dispose() { }
    }

    private sealed class ThrowingEmbeddingFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose)
            => throw new InvalidOperationException("embedding không khả dụng");
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> Tokens(params string[] tokens)
    {
        foreach (string t in tokens)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, t);
        }
    }

#pragma warning disable CS1998 // async iterator giả lập luồng ném lỗi giữa chừng
    private static async IAsyncEnumerable<ChatResponseUpdate> Throwing(Exception ex,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (ct.IsCancellationRequested) yield break;
        throw ex;
    }
#pragma warning restore CS1998

    private string TempDb()
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db");
        _tempDbPaths.Add(path);
        return path;
    }

    private MainViewModel BuildVm(bool hasKey, Func<IAsyncEnumerable<ChatResponseUpdate>> stream,
        FakeChatHistoryStore chat, FakeWorkspaceStore? wsStore = null)
    {
        var vm = new MainViewModel(
            new ITextPdfDocumentService(),
            new FakeSettings(hasKey),
            new FakeChatClientFactory(stream),
            new SqliteDocumentIndex(TempDb(), Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
            new AiEmbeddingGeneratorFactory(),
            chatHistory: chat,
            workspaceStore: wsStore ?? new FakeWorkspaceStore());
        _viewModels.Add(vm);
        return vm;
    }

    // MainViewModel.Dispose đóng SqliteDocumentIndex, nên chỉ còn việc xoá file .db tạm.
    public void Dispose()
    {
        foreach (var vm in _viewModels) vm.Dispose();
        _tempDbPaths.AddRange(_tempFiles);
        foreach (string path in _tempDbPaths)
        {
            foreach (string file in new[] { path, path + "-wal", path + "-shm" })
            {
                try { File.Delete(file); }
                catch (IOException) { /* dọn dẹp best-effort */ }
                catch (UnauthorizedAccessException) { /* dọn dẹp best-effort */ }
            }
        }
    }

    [Fact]
    public async Task SendMessage_WhitespaceInput_DoesNothing()
    {
        var vm = BuildVm(hasKey: true, () => Tokens("x"), new FakeChatHistoryStore());
        vm.Chat.Input = "   ";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Single(vm.Chat.Messages); // chỉ còn lời chào
        Assert.Equal("   ", vm.Chat.Input);
    }

    [Fact]
    public async Task SendMessage_NotConfigured_AddsUserAndNotConfiguredAiMessage_AndClearsInput()
    {
        var vm = BuildVm(hasKey: false, () => Tokens("x"), new FakeChatHistoryStore());
        vm.Chat.Input = "Xin chào";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(string.Empty, vm.Chat.Input);
        Assert.Equal(3, vm.Chat.Messages.Count);
        Assert.Equal("User", vm.Chat.Messages[1].Role);
        Assert.Equal("Xin chào", vm.Chat.Messages[1].Content);
        Assert.Equal("AI", vm.Chat.Messages[2].Role);
        Assert.Equal(NotConfigured, vm.Chat.Messages[2].Content);
    }

    [Fact]
    public async Task SendMessage_Configured_StreamsTokensIntoAiMessage()
    {
        var vm = BuildVm(hasKey: true, () => Tokens("Xin ", "chào"), new FakeChatHistoryStore());
        vm.Chat.Input = "Hỏi";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(3, vm.Chat.Messages.Count);
        Assert.Equal("AI", vm.Chat.Messages[2].Role);
        Assert.Equal("Xin chào", vm.Chat.Messages[2].Content);
    }

    [Fact]
    public async Task SendMessage_NetworkFailure_ShowsMappedNetworkError()
    {
        var vm = BuildVm(hasKey: true, () => Throwing(new System.Net.Http.HttpRequestException("down")), new FakeChatHistoryStore());
        vm.Chat.Input = "Hỏi";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal("Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.", vm.Chat.Messages[2].Content);
    }

    [Fact]
    public async Task SendMessage_CanBeSentAgainAfterFailure()
    {
        var vm = BuildVm(hasKey: true, () => Throwing(new System.Net.Http.HttpRequestException("down")), new FakeChatHistoryStore());
        vm.Chat.Input = "Một";
        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        vm.Chat.Input = "Hai";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(5, vm.Chat.Messages.Count);
    }

    [Fact]
    public async Task SendMessage_NoDocumentNorWorkspace_DoesNotPersistTurn()
    {
        var chat = new FakeChatHistoryStore();
        var vm = BuildVm(hasKey: true, () => Tokens("Đáp"), chat);
        vm.Chat.Input = "Hỏi";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Empty(chat.Appended);
    }

    [Fact]
    public async Task SendMessage_InWorkspaceSession_PersistsUserAndAiTurnsUnderWorkspaceId()
    {
        var chat = new FakeChatHistoryStore();
        var wsStore = new FakeWorkspaceStore();
        var vm = BuildVm(hasKey: true, () => Tokens("Đáp ", "án"), chat, wsStore);
        var workspace = new Workspace(Id: "ws-chat", Name: "Dự án", IsDefault: false, DefaultDocumentId: null, CreatedAtUnixMs: 1, UpdatedAtUnixMs: 1);
        wsStore.Upsert(workspace);
        wsStore.AddDocument(workspace.Id, "docA");
        vm.Library.Add(new LibraryItem("docA", "Sách A", "/lib/a.pdf", ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));
        vm.OpenWorkspaceCommand.Execute(workspace);
        chat.Appended.Clear();
        vm.Chat.Input = "Hỏi";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(
            [("ws-chat", "User", "Hỏi"), ("ws-chat", "AI", "Đáp án")],
            chat.Appended);
    }

    [Fact]
    public void Constructor_WiresDefaultSurfacesAndState()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = BuildVm(hasKey: false, () => Tokens("x"), new FakeChatHistoryStore(), wsStore);

        Assert.NotNull(vm.Tabs);
        Assert.NotNull(vm.Notes);
        Assert.NotNull(vm.DocumentsSurface);
        Assert.False(vm.IsDarkMode);
        Assert.Single(vm.Chat.Messages); // lời chào
        Assert.Equal("AI", vm.Chat.Messages[0].Role);
        Assert.Null(vm.ActiveWorkspaceId);
    }

    private (MainViewModel vm, FakeWorkspaceStore store) WorkspaceSessionWithDocA()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = BuildVm(hasKey: false, () => Tokens("x"), new FakeChatHistoryStore(), wsStore);
        var workspace = new Workspace(Id: "ws-tabs", Name: "Tab", IsDefault: false, DefaultDocumentId: null, CreatedAtUnixMs: 1, UpdatedAtUnixMs: 1);
        wsStore.Upsert(workspace);
        wsStore.AddDocument(workspace.Id, "docA");
        vm.Library.Add(new LibraryItem("docA", "Sách A", "/lib/a.pdf", ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));
        vm.OpenWorkspaceCommand.Execute(workspace);
        wsStore.OpenSets.Clear();
        return (vm, wsStore);
    }

    [Fact]
    public void OpenSet_IsSaved_WhenTabOpened()
    {
        var (vm, store) = WorkspaceSessionWithDocA();

        vm.Tabs.OpenOrActivate("docB", "B", "/lib/b.pdf", initialPage: 1);

        var saved = store.OpenSets["ws-tabs"];
        Assert.Contains(saved, t => string.Equals(t.DocumentId, "docB", StringComparison.Ordinal));
        Assert.Equal(vm.Tabs.Tabs.Count, saved.Count);
    }

    [Fact]
    public void OpenSet_IsSaved_WhenActiveTabChanges()
    {
        var (vm, store) = WorkspaceSessionWithDocA();
        var tabA = vm.Tabs.Tabs.First(t => string.Equals(t.DocumentId, "docA", StringComparison.Ordinal));
        vm.Tabs.OpenOrActivate("docB", "B", "/lib/b.pdf", initialPage: 1);
        store.OpenSets.Clear();

        vm.Tabs.ActivateTab(tabA);

        var saved = store.OpenSets["ws-tabs"];
        Assert.True(saved.Single(t => string.Equals(t.DocumentId, "docA", StringComparison.Ordinal)).IsActive);
        Assert.False(saved.Single(t => string.Equals(t.DocumentId, "docB", StringComparison.Ordinal)).IsActive);
    }

    [Fact]
    public void OpenSet_IsSaved_WhenTabClosed()
    {
        var (vm, store) = WorkspaceSessionWithDocA();
        var tabB = vm.Tabs.OpenOrActivate("docB", "B", "/lib/b.pdf", initialPage: 1);
        store.OpenSets.Clear();

        vm.Tabs.Close(tabB);

        var saved = store.OpenSets["ws-tabs"];
        Assert.DoesNotContain(saved, t => string.Equals(t.DocumentId, "docB", StringComparison.Ordinal));
    }

    // Mở một tài liệu thật (file tạm) qua dịch vụ PDF giả để _documentId khác null.
    private (MainViewModel vm, FakeChatHistoryStore chat) OpenedDocumentVm(
        DocumentIndexStatus indexStatus, FakeChatHistoryStore? seeded = null)
    {
        var chat = seeded ?? new FakeChatHistoryStore();
        var vm = new MainViewModel(
            new FakeDocumentService(),
            new FakeSettings(hasKey: true),
            new FakeChatClientFactory(() => Tokens("Đáp ", "án")),
            new FakeDocumentIndex(indexStatus),
            new ThrowingEmbeddingFactory(),
            chatHistory: chat,
            workspaceStore: new FakeWorkspaceStore());
        _viewModels.Add(vm);
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        File.WriteAllText(path, "nội dung giả " + Guid.NewGuid());
        _tempFiles.Add(path);
        vm.OpenLibraryItemCommand.Execute(
            new LibraryItem("docOpen", "Sách mở", path, ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));
        chat.Appended.Clear();
        return (vm, chat);
    }

    [Theory]
    [InlineData(DocumentIndexStatus.Complete)] // RAG ném lỗi khi tạo embedding
    [InlineData(DocumentIndexStatus.None)]     // RAG trả null vì chưa lập chỉ mục
    public async Task SendMessage_WithOpenDocument_StreamsAnswerAndPersistsUnderDocumentId_WhenRagFailsOrIsEmpty(
        DocumentIndexStatus indexStatus)
    {
        var (vm, chat) = OpenedDocumentVm(indexStatus);
        string? documentId = vm.CurrentDocumentId;
        Assert.NotNull(documentId);
        vm.Chat.Input = "Hỏi";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal("Đáp án", vm.Chat.Messages[^1].Content);
        Assert.Equal(
            [(documentId, "User", "Hỏi"), (documentId, "AI", "Đáp án")],
            chat.Appended);
    }

    [Fact]
    public void OpenDocument_WithStoredHistory_ShowsStoredMessagesInsteadOfGreeting()
    {
        var seeded = new FakeChatHistoryStore();
        seeded.Stored.Add(new ChatHistoryEntry("docOpen", "User", "Câu hỏi cũ", 1));
        seeded.Stored.Add(new ChatHistoryEntry("docOpen", "AI", "Trả lời cũ", 1));

        var (vm, _) = OpenedDocumentVm(DocumentIndexStatus.None, seeded);

        Assert.Equal(["Câu hỏi cũ", "Trả lời cũ"], vm.Chat.Messages.Select(m => m.Content), StringComparer.Ordinal);
    }

    [Fact]
    public void OpenCitationSource_SetsSourcePassageOnItsPage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();

        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "đoạn nguồn được trích"));

        Assert.NotNull(vm.ActiveSourcePassage);
        Assert.Equal("docA", vm.ActiveSourcePassage.DocumentId);
        Assert.Equal(2, vm.ActiveSourcePassage.PageIndex);
        Assert.Equal(["đoạn nguồn được trích"], vm.ActiveSourcePassage.Fragments);
    }

    [Fact]
    public void OpenCitationSource_WithQuote_SetsQuoteFragmentsAndKeepsChunkAsFallback()
    {
        var (vm, _) = WorkspaceSessionWithDocA();

        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "cả đoạn nguồn dài"), "câu được trích");

        Assert.Equal(["câu được trích"], vm.ActiveSourcePassage?.QuoteFragments);
        Assert.Equal(["cả đoạn nguồn dài"], vm.ActiveSourcePassage?.Fragments);
    }

    [Fact]
    public void OpenCitationSource_SourceSavedWithoutPassage_ClearsPreviousSourcePassage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();
        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "đoạn nguồn"));

        vm.OpenCitationSource(new RagSource(2, "docA", "Sách A", 5));

        Assert.Null(vm.ActiveSourcePassage);
    }

    [Fact]
    public void OpenCitationSource_DocumentNotInLibrary_ClearsStalePassage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();
        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "đoạn nguồn"));

        vm.OpenCitationSource(new RagSource(2, "đã-xoá", "Sách đã xoá", 1, "đoạn khác"));

        Assert.Null(vm.ActiveSourcePassage);
    }

    [Fact]
    public void OpenCitationSource_InCurrentDocument_KeepsNewPassage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();
        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "đoạn một"));

        vm.OpenCitationSource(new RagSource(2, "docA", "Sách A", 3, "đoạn hai"));

        Assert.Equal(3, vm.ActiveSourcePassage?.PageIndex);
    }

    [Fact]
    public void OpenCitationSource_InAnotherDocument_ActivatesItAndKeepsItsPassage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();
        vm.Library.Add(new LibraryItem("docB", "Sách B", "/lib/b.pdf", ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));

        vm.OpenCitationSource(new RagSource(1, "docB", "Sách B", 4, "đoạn của B"));

        Assert.Equal("docB", vm.ActiveSourcePassage?.DocumentId);
    }

    [Fact]
    public void ActiveTabChange_ClearsSourcePassage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();
        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "đoạn nguồn"));

        vm.Tabs.OpenOrActivate("docB", "B", "/lib/b.pdf", initialPage: 1);

        Assert.Null(vm.ActiveSourcePassage);
    }

    [Fact]
    public void OpeningAnotherFileStandalone_ClearsSourcePassage()
    {
        var (vm, _) = OpenedDocumentVm(DocumentIndexStatus.None);
        vm.ActiveSourcePassage = new SourcePassage("docOpen", 1, ["đoạn"]);

        vm.OpenLibraryItemCommand.Execute(
            new LibraryItem("docOpen", "Sách mở", vm.FilePath!, ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));

        Assert.Null(vm.ActiveSourcePassage);
    }

    [Fact]
    public void ClearSourcePassageCommand_DismissesPassage()
    {
        var (vm, _) = WorkspaceSessionWithDocA();
        vm.OpenCitationSource(new RagSource(1, "docA", "Sách A", 2, "đoạn nguồn"));

        vm.ClearSourcePassageCommand.Execute(parameter: null);

        Assert.Null(vm.ActiveSourcePassage);
    }
}
