using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;

namespace PdfReaderApp.Tests.ViewModels;

/// <summary>Chat theo phạm vi (#107): mặc định Tài liệu này giữ hành vi cũ; Toàn Workspace fan-out qua
/// các tài liệu đã index, trả bảng nguồn [n] và lập chỉ mục nền cho tài liệu còn thiếu.</summary>
public sealed class ChatViewModelWorkspaceTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    private static readonly string Model = AiModelSettings.Default.EmbeddingModel;

    private readonly List<ChatViewModel> _chats = [];
    private readonly List<List<AiMessage>> _sent = [];
    private readonly Dictionary<string, DocumentIndexStatus> _statuses = [];
    private readonly Dictionary<string, List<ScoredChunk>> _hits = [];
    private readonly List<string> _indexedInBackground = [];
    private readonly TaskCompletionSource _indexingGate = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IReadOnlyList<WorkspaceDocument> _documents = [];
    private int _documentsRequested;
    private bool _ragThrows;

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

    private sealed class FakeChatClientFactory(List<List<AiMessage>> sent) : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => new FakeChatClient(sent);
    }

    private sealed class FakeChatClient(List<List<AiMessage>> sent) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<AiMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AiMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            sent.Add([.. messages]);
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Theo [1] là vậy.");
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeHistory : IChatHistoryStore
    {
        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix) { }
        public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId) => [];
        public void DeleteForDocument(string documentId) { }
    }

    private sealed class FakeEmbeddingFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose) => new Gen();

        private sealed class Gen : IEmbeddingGenerator<string, Embedding<float>>
        {
            public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
                IEnumerable<string> values, EmbeddingGenerationOptions? options = null,
                CancellationToken cancellationToken = default)
                => Task.FromResult(new GeneratedEmbeddings<Embedding<float>>(
                    values.Select(_ => new Embedding<float>(new float[] { 1f, 0f })).ToList()));

            public object? GetService(Type serviceType, object? serviceKey = null) => null;
            public void Dispose() { }
        }
    }

    private sealed class FakeIndex(ChatViewModelWorkspaceTests owner) : IDocumentIndex
    {
        public bool VectorSearchAvailable => true;
        public bool TryBeginIndexing(string documentId, string embeddingModel) => true;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string d, string m)
            => m == Model && owner._statuses.TryGetValue(d, out var s) ? s : DocumentIndexStatus.None;
        public void DeleteDocument(string d) { }
        public IReadOnlyList<long> WriteChunks(string d, string? f, int p, IReadOnlyList<Chunk> c) => [];
        public IReadOnlyList<StoredChunk> GetChunks(string d) => [];
        public void DeleteEmbeddings(string d, string m) { }
        public void WriteEmbeddings(string m, IReadOnlyList<(long, float[])> e) { }
        public void SetStatus(string d, DocumentIndexStatus s, string m) { }
        public IReadOnlyList<SearchResult> SearchText(string d, string q, int l = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string d, string m, float[] v, int k = 5)
        {
            if (owner._ragThrows) throw new InvalidOperationException("index hỏng");
            return owner._hits.TryGetValue(d, out var h) ? [.. h.Select(s => s.Chunk)] : [];
        }
        public IReadOnlyList<ScoredChunk> RetrieveRelevantScored(string d, string m, float[] v, int k = 5)
        {
            if (owner._ragThrows) throw new InvalidOperationException("index hỏng");
            return owner._hits.TryGetValue(d, out var h) ? h : [];
        }
        public void Dispose() { }
    }

    private static ScoredChunk Hit(string doc, int page, string text, double distance)
        => new(new Chunk(doc, page, 0, text), distance);

    private static WorkspaceDocument Doc(string id, string title) => new(id, title, $"C:\\lib\\{id}.pdf");

    private ChatViewModel CreateChat(string? currentDocumentId = "a")
    {
        _statuses["a"] = DocumentIndexStatus.Complete;
        _statuses["b"] = DocumentIndexStatus.Complete;
        _hits["a"] = [Hit("a", 0, "nội dung A", 0.1)];
        _hits["b"] = [Hit("b", 2, "nội dung B", 0.4)];
        _documents = [Doc("a", "Sách A"), Doc("b", "Sách B")];

        var settings = new FakeSettings();
        var runner = new WorkspaceIndexingRunner(
            async (document, ct) =>
            {
                _indexedInBackground.Add(document.DocumentId);
                await _indexingGate.Task.WaitAsync(ct);
                return DocumentIndexStatus.Complete;
            },
            postToUiThread: action => action());
        var chat = new ChatViewModel(
            new AiChatService(settings, new FakeChatClientFactory(_sent)),
            new FakeHistory(),
            new RagContextService(new FakeIndex(this), new FakeEmbeddingFactory(), settings),
            () => new ChatContext(currentDocumentId, "ws1", [], 1),
            new ChatWorkspace(() => { _documentsRequested++; return _documents; }, runner));
        _chats.Add(chat);
        return chat;
    }

    private static async Task AskAsync(ChatViewModel chat, string question)
    {
        chat.Input = question;
        await chat.SendMessageCommand.ExecuteAsync(null).WaitAsync(Timeout);
    }

    private string LastUserTurn() => _sent[^1][^1].Text;

    [Fact]
    public void Scope_DefaultsToCurrentDocument()
    {
        var chat = CreateChat();

        Assert.Equal(DocumentScope.CurrentDocument, chat.Scope);
        Assert.True(chat.IsCurrentDocumentScope);
        Assert.False(chat.IsWorkspaceScope);
    }

    [Fact]
    public void SetScopeCommand_Workspace_SwitchesScope()
    {
        var chat = CreateChat();

        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        Assert.Equal(DocumentScope.Workspace, chat.Scope);
        Assert.True(chat.IsWorkspaceScope);
        Assert.False(chat.IsCurrentDocumentScope);
    }

    [Fact]
    public async Task CurrentDocumentScope_KeepsSingleDocumentBehaviourAndNeverTouchesWorkspace()
    {
        var chat = CreateChat();

        await AskAsync(chat, "câu hỏi");

        Assert.Contains("[Trang 1] nội dung A", LastUserTurn(), StringComparison.Ordinal);
        Assert.DoesNotContain("Tài liệu: \"", LastUserTurn(), StringComparison.Ordinal);
        Assert.Equal(0, _documentsRequested);
        Assert.Empty(chat.Messages.Last().Sources);
    }

    [Fact]
    public async Task WorkspaceScope_SendsNumberedContextFromManyDocumentsToTheModel()
    {
        var chat = CreateChat();
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        string turn = LastUserTurn();
        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 1\nnội dung A", turn, StringComparison.Ordinal);
        Assert.Contains("[2] Tài liệu: \"Sách B\", Trang: 3\nnội dung B", turn, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceScope_AnswerMessageCarriesSourceTable()
    {
        var chat = CreateChat();
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        var answer = chat.Messages.Last();
        Assert.Equal("AI", answer.Role);
        Assert.Equal(
            [new RagSource(1, "a", "Sách A", 0), new RagSource(2, "b", "Sách B", 2)],
            answer.Sources);
    }

    [Fact]
    public async Task SystemPrompt_AsksModelToCiteWithBracketNumbers()
    {
        var chat = CreateChat();

        await AskAsync(chat, "câu hỏi");

        string system = _sent[0][0].Text;
        Assert.Contains("[n]", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceScope_UnindexedDocuments_AreQueuedForBackgroundIndexingAndNoticeShown()
    {
        var chat = CreateChat();
        _statuses["c"] = DocumentIndexStatus.None;
        _statuses["d"] = DocumentIndexStatus.Partial;
        _documents = [Doc("a", "Sách A"), Doc("b", "Sách B"), Doc("c", "Sách C"), Doc("d", "Sách D")];
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        Assert.Equal("Đang lập chỉ mục 2 tài liệu, câu trả lời chưa gồm các tài liệu này.", chat.WorkspaceIndexing.StatusText);
        await WaitUntilAsync(() => _indexedInBackground.Count >= 1);
        Assert.Equal("c", _indexedInBackground[0]);
        // Câu hỏi hiện tại không chờ: vẫn trả lời bằng tài liệu đã Complete.
        Assert.Contains("nội dung A", LastUserTurn(), StringComparison.Ordinal);
        Assert.DoesNotContain("Sách C", LastUserTurn(), StringComparison.Ordinal);
        _indexingGate.SetResult();
        await chat.WorkspaceIndexing.BackgroundTask!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WorkspaceScope_NothingIndexedYet_FallsBackToCurrentPageContextAndStillIndexes()
    {
        var chat = CreateChat();
        _statuses["a"] = DocumentIndexStatus.None;
        _statuses["b"] = DocumentIndexStatus.None;
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        Assert.Equal("AI", chat.Messages.Last().Role);
        Assert.Empty(chat.Messages.Last().Sources);
        Assert.Contains("Đang lập chỉ mục 2 tài liệu", chat.WorkspaceIndexing.StatusText, StringComparison.Ordinal);
        _indexingGate.SetResult();
        await chat.WorkspaceIndexing.BackgroundTask!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WorkspaceScope_RagFails_StillAnswersWithoutSources()
    {
        var chat = CreateChat();
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);
        _ragThrows = true;

        await AskAsync(chat, "câu hỏi");

        Assert.Equal("AI", chat.Messages.Last().Role);
        Assert.Contains("Theo [1] là vậy.", chat.Messages.Last().Content, StringComparison.Ordinal);
        Assert.Empty(chat.Messages.Last().Sources);
    }

    [Fact]
    public async Task LoadHistory_OwnerChanges_CancelsBackgroundIndexingAndClearsNotice()
    {
        string owner = "ws1";
        var settings = new FakeSettings();
        var runner = new WorkspaceIndexingRunner(
            async (_, ct) =>
            {
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
                return DocumentIndexStatus.Complete;
            },
            postToUiThread: action => action());
        using var chat = new ChatViewModel(
            new AiChatService(settings, new FakeChatClientFactory(_sent)),
            new FakeHistory(),
            new RagContextService(new FakeIndex(this), new FakeEmbeddingFactory(), settings),
            () => new ChatContext("a", owner, [], 1),
            new ChatWorkspace(() => [], runner));
        chat.LoadHistory();
        runner.Enqueue([Doc("c", "Sách C")]);
        Assert.NotEmpty(runner.StatusText);

        owner = "ws2";
        chat.LoadHistory();

        Assert.Equal(string.Empty, runner.StatusText);
        await runner.BackgroundTask!.WaitAsync(Timeout);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + Timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Điều kiện không thoả trong thời gian chờ.");
            await Task.Delay(5);
        }
    }

    public void Dispose()
    {
        _indexingGate.TrySetResult();
        foreach (var chat in _chats) chat.Dispose();
        GC.SuppressFinalize(this);
    }
}
