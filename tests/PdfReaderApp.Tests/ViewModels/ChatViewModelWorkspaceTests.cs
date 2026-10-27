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
    private bool _embedThrows;
    private string? _embeddingKey = "key";

    // Chat dùng Claude (luôn có key), Embedding dùng OpenAI (key do test đặt) để thiếu key embedding mà chat vẫn chạy.
    private sealed class FakeSettings(Func<string?> embeddingKey) : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => provider == AiProvider.Claude ? "key" : embeddingKey();
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(GetApiKey(provider));
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => new(
            AiProvider.Claude, "claude-test", AiModelSettings.Default.EmbeddingProvider, AiModelSettings.Default.EmbeddingModel);
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
        public List<ChatHistoryEntry> Entries { get; } = [];
        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix,
            IReadOnlyList<RagSource>? sources = null)
            => Entries.Add(new ChatHistoryEntry(documentId, role, content, createdAtUnix, sources ?? []));
        public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId) => Entries;
        public void DeleteForDocument(string documentId) { }
    }

    private sealed class FakeEmbeddingFactory(ChatViewModelWorkspaceTests owner) : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options, EmbeddingPurpose purpose)
            => owner._embedThrows ? throw new InvalidOperationException("embedding hỏng") : new Gen();

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
        public bool TryBeginIndexing(string documentId, string embeddingModel, out long claimToken)
        {
            claimToken = 0;
            return true;
        }
        public void ReleaseIndexing(string documentId, string embeddingModel, long claimToken) { }
        public bool WriteEmbeddingsIfClaimed(string documentId, string embeddingModel, long claimToken, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) => true;
        public bool FinishIndexing(string documentId, string embeddingModel, long claimToken, DocumentIndexStatus status) => true;
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

    private readonly FakeHistory _history = new();
    private readonly List<(RagSource Source, string? Quote)> _openedSources = [];

    private ChatViewModel CreateChat(string? currentDocumentId = "a", IReadOnlyList<TextBlock>? blocks = null)
    {
        _statuses["a"] = DocumentIndexStatus.Complete;
        _statuses["b"] = DocumentIndexStatus.Complete;
        _hits["a"] = [Hit("a", 0, "nội dung A", 0.1)];
        _hits["b"] = [Hit("b", 2, "nội dung B", 0.4)];
        _documents = [Doc("a", "Sách A"), Doc("b", "Sách B")];

        var settings = new FakeSettings(() => _embeddingKey);
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
            _history,
            new RagContextService(new FakeIndex(this), new FakeEmbeddingFactory(this), settings),
            () => new ChatContext(currentDocumentId, "ws1", blocks ?? [], 1, "Sách A"),
            new ChatWorkspace(() => { _documentsRequested++; return _documents; }, runner),
            (source, quote) => _openedSources.Add((source, quote)));
        _chats.Add(chat);
        return chat;
    }

    private static TextBlock PageBlock(int page, string text) => new(text, 0, 0, 10, 10, 12, page, "P");

    [Fact]
    public async Task CurrentDocumentScope_NotIndexed_PageContextIsNumberedAndAnswerHasClickableSources()
    {
        var chat = CreateChat(blocks: [PageBlock(0, "chữ trang một")]);
        _statuses["a"] = DocumentIndexStatus.None;

        await AskAsync(chat, "câu hỏi");

        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 1\nchữ trang một", LastUserTurn(), StringComparison.Ordinal);
        Assert.Equal([new RagSource(1, "a", "Sách A", 0, "chữ trang một")], chat.Messages.Last().Sources);
    }

    [Fact]
    public async Task WorkspaceScope_RagFails_PageContextIsNumberedAndNoticeStays()
    {
        var chat = CreateChat(blocks: [PageBlock(0, "chữ trang một")]);
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);
        _ragThrows = true;

        await AskAsync(chat, "câu hỏi");

        Assert.Equal([new RagSource(1, "a", "Sách A", 0, "chữ trang một")], chat.Messages.Last().Sources);
        Assert.NotEmpty(chat.ScopeNotice);
    }

    [Fact]
    public async Task CurrentDocumentScope_NoDocumentId_PageContextStaysUnnumbered()
    {
        var chat = CreateChat(currentDocumentId: null, blocks: [PageBlock(0, "chữ trang một")]);

        await AskAsync(chat, "câu hỏi");

        Assert.DoesNotContain("[1]", LastUserTurn(), StringComparison.Ordinal);
        Assert.Contains("chữ trang một", LastUserTurn(), StringComparison.Ordinal);
        Assert.Empty(chat.Messages.Last().Sources);
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
    public async Task CurrentDocumentScope_CitesNumberedSourcesOfThatDocumentOnlyAndNeverTouchesWorkspace()
    {
        var chat = CreateChat();

        await AskAsync(chat, "câu hỏi");

        Assert.Contains("[1] Tài liệu: \"Sách A\", Trang: 1\nnội dung A", LastUserTurn(), StringComparison.Ordinal);
        Assert.DoesNotContain("nội dung B", LastUserTurn(), StringComparison.Ordinal);
        Assert.Equal(0, _documentsRequested);
        Assert.Equal([new RagSource(1, "a", "Sách A", 0, "nội dung A")], chat.Messages.Last().Sources);
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
            [new RagSource(1, "a", "Sách A", 0, "nội dung A"), new RagSource(2, "b", "Sách B", 2, "nội dung B")],
            answer.Sources);
    }

    [Fact]
    public async Task WorkspaceScope_AnswerPersistsSourceTableWithTheTurn()
    {
        var chat = CreateChat();
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        var ai = _history.Entries.Single(e => e.Role == "AI");
        Assert.Equal([new RagSource(1, "a", "Sách A", 0, "nội dung A"), new RagSource(2, "b", "Sách B", 2, "nội dung B")], ai.Sources);
        Assert.Empty(_history.Entries.Single(e => e.Role == "User").Sources);
    }

    [Fact]
    public void LoadHistory_RestoresSourceTableSoCitationsStayClickable()
    {
        RagSource[] sources = [new(1, "a", "Sách A", 0)];
        _history.Entries.Add(new ChatHistoryEntry("ws1", "AI", "Theo [1].", 1, sources));
        var chat = CreateChat();

        chat.LoadHistory();

        var message = chat.Messages.Single();
        Assert.Equal(sources, message.Sources);
        Assert.Contains(message.Spans, s => s.Source == sources[0]);
    }

    [Fact]
    public async Task Spans_FollowStreamedContent()
    {
        var chat = CreateChat();
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        var answer = chat.Messages.Last();
        Assert.Single(answer.Spans, s => s.Source is not null);
        Assert.Equal(" là vậy.", answer.Spans[^1].Text);
    }

    [Fact]
    public void OpenCitationCommand_OpensTheCitedSourceWithItsPassage()
    {
        var chat = CreateChat();
        var source = new RagSource(2, "b", "Sách B", 2, "nội dung B");

        chat.OpenCitationCommand.Execute(CitationSpan.ForSource(source));

        Assert.Equal([(source, (string?)null)], _openedSources);
    }

    [Fact]
    public void OpenCitationCommand_PlainTextSpan_OpensNothing()
    {
        var chat = CreateChat();

        chat.OpenCitationCommand.Execute(new CitationSpan("chữ thường"));

        Assert.Empty(_openedSources);
    }

    [Fact]
    public void OpenCitationCommand_PassesTheQuoteOfThatCitation()
    {
        var chat = CreateChat();
        var source = new RagSource(2, "b", "Sách B", 2, "nội dung B");

        chat.OpenCitationCommand.Execute(CitationSpan.ForSource(source, "câu trích"));

        Assert.Equal([(source, "câu trích")], _openedSources);
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
    public async Task SystemPrompt_AsksModelToQuoteTheSentenceVerbatim()
    {
        var chat = CreateChat();

        await AskAsync(chat, "câu hỏi");

        string system = _sent[0][0].Text;
        Assert.Contains("[n: \"", system, StringComparison.Ordinal);
        Assert.Contains("nguyên văn", system, StringComparison.Ordinal);
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
        var settings = new FakeSettings(() => _embeddingKey);
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
            new RagContextService(new FakeIndex(this), new FakeEmbeddingFactory(this), settings),
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

    private const string PageOnlyNotice = "chỉ dựa trên trang đang đọc";

    [Fact]
    public async Task WorkspaceScope_NoEmbeddingKey_DoesNotQueueIndexingAndNoticeAsksForTheKey()
    {
        var chat = CreateChat();
        _statuses["a"] = DocumentIndexStatus.None;
        _statuses["b"] = DocumentIndexStatus.None;
        _embeddingKey = null;
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        Assert.Empty(_indexedInBackground);
        Assert.Equal(string.Empty, chat.WorkspaceIndexing.StatusText);
        Assert.Contains(AiErrorMessages.NotConfigured(AiModelSettings.Default.EmbeddingProvider), chat.Notice, StringComparison.Ordinal);
        Assert.Contains(PageOnlyNotice, chat.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceScope_NothingUsable_NoticeSaysAnswerOnlyUsesCurrentPage()
    {
        var chat = CreateChat();
        _statuses["a"] = DocumentIndexStatus.None;
        _statuses["b"] = DocumentIndexStatus.None;
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        Assert.Contains(PageOnlyNotice, chat.Notice, StringComparison.Ordinal);
        _indexingGate.SetResult();
        await chat.WorkspaceIndexing.BackgroundTask!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WorkspaceScope_RagFails_NoticeSaysAnswerOnlyUsesCurrentPage()
    {
        var chat = CreateChat();
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);
        _ragThrows = true;

        await AskAsync(chat, "câu hỏi");

        Assert.Contains(PageOnlyNotice, chat.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WorkspaceScope_EmbeddingFails_StillQueuesUnindexedDocumentsBeforeEmbedding()
    {
        var chat = CreateChat();
        _statuses["c"] = DocumentIndexStatus.None;
        _documents = [Doc("a", "Sách A"), Doc("c", "Sách C")];
        _embedThrows = true;
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        await WaitUntilAsync(() => _indexedInBackground.Count >= 1);
        Assert.Equal("c", _indexedInBackground[0]);
        _indexingGate.SetResult();
        await chat.WorkspaceIndexing.BackgroundTask!.WaitAsync(Timeout);
    }

    [Fact]
    public async Task WorkspaceScope_DocumentBeingIndexedElsewhere_IsMentionedInTheNoticeForThatQuestion()
    {
        var chat = CreateChat();
        _statuses["e"] = DocumentIndexStatus.Indexing;
        _documents = [Doc("a", "Sách A"), Doc("e", "Sách E")];
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi");

        Assert.Equal(
            "Khi bạn hỏi, 1 tài liệu đang được lập chỉ mục ở nơi khác nên câu trả lời này chưa gồm tài liệu đó.",
            chat.Notice);
        Assert.Equal(string.Empty, chat.WorkspaceIndexing.StatusText);
    }

    [Fact]
    public async Task WorkspaceScope_AskedTwiceWhileRunnerIndexes_CountsEachDocumentOnceAndEndsWithDoneText()
    {
        var chat = CreateChat();
        _statuses["c"] = DocumentIndexStatus.None;
        _documents = [Doc("a", "Sách A"), Doc("c", "Sách C")];
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);

        await AskAsync(chat, "câu hỏi một");
        await WaitUntilAsync(() => _indexedInBackground.Count >= 1);
        _statuses["c"] = DocumentIndexStatus.Indexing; // lập chỉ mục thật giữ claim của chính hàng đợi
        await AskAsync(chat, "câu hỏi hai");

        // Tài liệu do chính hàng đợi xử lý không được tính lại như "ở nơi khác".
        Assert.Equal("Đang lập chỉ mục 1 tài liệu, câu trả lời chưa gồm các tài liệu này.", chat.Notice);

        _statuses["c"] = DocumentIndexStatus.Complete;
        _indexingGate.SetResult();
        await chat.WorkspaceIndexing.BackgroundTask!.WaitAsync(Timeout);

        Assert.Equal("Đã lập chỉ mục xong 1 tài liệu. Hỏi lại để câu trả lời gồm cả các tài liệu này.", chat.Notice);
    }

    [Fact]
    public async Task Notice_NextQuestionWithEverythingIndexed_NoLongerShowsPageOnlyWarning()
    {
        var chat = CreateChat();
        _statuses["a"] = DocumentIndexStatus.None;
        _statuses["b"] = DocumentIndexStatus.None;
        chat.SetScopeCommand.Execute(DocumentScope.Workspace);
        await AskAsync(chat, "câu hỏi một");
        _indexingGate.SetResult();
        await chat.WorkspaceIndexing.BackgroundTask!.WaitAsync(Timeout);
        _statuses["a"] = DocumentIndexStatus.Complete;
        _statuses["b"] = DocumentIndexStatus.Complete;

        await AskAsync(chat, "câu hỏi hai");

        Assert.DoesNotContain(PageOnlyNotice, chat.Notice, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CurrentDocumentScope_NeverShowsWorkspaceNotices()
    {
        var chat = CreateChat();
        _statuses["a"] = DocumentIndexStatus.None;

        await AskAsync(chat, "câu hỏi");

        Assert.Equal(string.Empty, chat.Notice);
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
