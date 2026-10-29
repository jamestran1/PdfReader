using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Ghim hành vi huỷ yêu cầu chat đang chạy (#214, CS-13): đổi tài liệu hoặc gửi câu hỏi mới
/// phải huỷ yêu cầu cũ và không ghi câu trả lời cũ vào lịch sử của tài liệu khác.</summary>
public sealed class ChatCancellationTests : IDisposable
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    private readonly List<MainViewModel> _viewModels = [];
    private readonly List<string> _tempFiles = [];
    private readonly List<List<AiMessage>> _sentMessages = [];

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

    private sealed class FakeChatClientFactory(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream, List<List<AiMessage>> sentMessages) : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => new FakeChatClient(stream, sentMessages);
    }

    private sealed class FakeChatClient(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream, List<List<AiMessage>> sentMessages) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<AiMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AiMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            sentMessages.Add([.. messages]); // chụp lại: _history của service còn bị sửa tiếp
            return stream(cancellationToken);
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeChatHistoryStore : IChatHistoryStore
    {
        public readonly List<(string owner, string role, string content)> Appended = [];
        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix, IReadOnlyList<PdfReaderApp.Models.RagSource>? sources = null)
            => Appended.Add((documentId, role, content));
        public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId) => [];
        public void DeleteForDocument(string documentId) { }
    }

    private sealed class FakeDocumentService : IPdfDocumentService
    {
        public void LoadFile(string filePath) { }
        public IReadOnlyList<TextBlock> ExtractStructure() => [];
        public IReadOnlyList<PageText> ExtractPageTexts() => [];
        public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query) => [];
        public IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments) => [];
        public PassageHits FindPassageHits(int pageIndex, IReadOnlyList<string> fragments) => new([], [.. fragments.Select(_ => false)]);
        public void Dispose() { }
    }

    private sealed class FakeDocumentIndex : IDocumentIndex
    {
        public bool VectorSearchAvailable => false;
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => DocumentIndexStatus.None;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks) => [];
        public IReadOnlyList<StoredChunk> GetChunks(string documentId) => [];
        public void WriteEmbeddings(string embeddingModel, IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void DeleteEmbeddings(string documentId, string embeddingModel) { }
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

    // Trả một token rồi treo cho tới khi bị huỷ: giả lập câu trả lời đang stream giữa chừng.
    private static async IAsyncEnumerable<ChatResponseUpdate> BlockingStream(
        TaskCompletionSource started, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Đáp cũ");
        started.TrySetResult();
        await Task.Delay(System.Threading.Timeout.Infinite, ct);
    }

    // Phản ứng huỷ chậm: giả lập continuation của yêu cầu cũ chạy trễ sau khi yêu cầu mới đã thêm lượt của nó.
    private static async IAsyncEnumerable<ChatResponseUpdate> SlowToCancelStream(
        TaskCompletionSource started, [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Đáp cũ");
        started.TrySetResult();
        try { await Task.Delay(System.Threading.Timeout.Infinite, ct); }
        catch (OperationCanceledException)
        {
            await Task.Delay(300, CancellationToken.None);
            throw;
        }
    }

    // Nhà cung cấp không tôn trọng token: sau khi bị huỷ vẫn kết thúc stream bình thường.
    private static async IAsyncEnumerable<ChatResponseUpdate> TokenIgnoringStream(
        TaskCompletionSource started, Task release)
    {
        yield return new ChatResponseUpdate(ChatRole.Assistant, "Đáp cũ");
        started.TrySetResult();
        await release;
    }

    // Huỷ làm kết nối đóng và SDK ném lỗi khác OCE trước khi có token nào: giả lập lỗi đọc HTTP.
    private static async IAsyncEnumerable<ChatResponseUpdate> FailsAfterReleaseStream(
        TaskCompletionSource started, Task release)
    {
        started.TrySetResult();
        await release;
        throw new IOException("kết nối bị đóng");
#pragma warning disable CS0162 // cần một yield để trình biên dịch coi đây là iterator
        yield break;
#pragma warning restore CS0162
    }

    private static async IAsyncEnumerable<ChatResponseUpdate> ShortStream(string text)
    {
        await Task.Yield();
        yield return new ChatResponseUpdate(ChatRole.Assistant, text);
    }

    private (MainViewModel vm, FakeChatHistoryStore chat) BuildVm(
        Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream)
    {
        var chat = new FakeChatHistoryStore();
        var vm = new MainViewModel(
            new FakeDocumentService(),
            new FakeSettings(),
            new FakeChatClientFactory(stream, _sentMessages),
            new FakeDocumentIndex(),
            new ThrowingEmbeddingFactory(),
            chatHistory: chat,
            workspaceStore: new FakeWorkspaceStore());
        _viewModels.Add(vm);
        return (vm, chat);
    }

    private string OpenDocument(MainViewModel vm, string id)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".pdf");
        File.WriteAllText(path, "nội dung giả " + Guid.NewGuid());
        _tempFiles.Add(path);
        vm.OpenLibraryItemCommand.Execute(
            new LibraryItem(id, "Sách " + id, path, ThumbPath: null, PageCount: 0, ImportedAtUnix: 1, LastOpenedAtUnix: 1));
        return vm.Session.DocumentId!;
    }

    public void Dispose()
    {
        foreach (var vm in _viewModels) vm.Dispose();
        foreach (string file in _tempFiles)
        {
            try { File.Delete(file); }
            catch (IOException) { /* dọn dẹp best-effort */ }
            catch (UnauthorizedAccessException) { /* dọn dẹp best-effort */ }
        }
    }

    [Fact]
    public async Task SwitchingDocument_CancelsInFlightAnswer_AndKeepsItOutOfNewDocumentHistory()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken seen = default;
        var (vm, chat) = BuildVm(ct => { seen = ct; return BlockingStream(started, ct); });
        OpenDocument(vm, "docA");
        vm.Chat.Input = "Hỏi";
        var send = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);

        OpenDocument(vm, "docB");

        Assert.True(seen.IsCancellationRequested);
        await send.WaitAsync(Timeout);
        Assert.Empty(chat.Appended);
        Assert.DoesNotContain(vm.Chat.Messages, m => m.Content.Contains("Đáp cũ", StringComparison.Ordinal));
        Assert.DoesNotContain(vm.Chat.Messages, m => m.Content.Contains("lỗi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SwitchingDocument_ThenAsking_PersistsOnlyNewAnswerUnderNewDocument()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var (vm, chat) = BuildVm(ct => ++calls == 1 ? BlockingStream(started, ct) : ShortStream("Đáp mới"));
        OpenDocument(vm, "docA");
        vm.Chat.Input = "Hỏi A";
        var send = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);
        string docB = OpenDocument(vm, "docB");
        await send.WaitAsync(Timeout);
        vm.Chat.Input = "Hỏi B";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null).WaitAsync(Timeout);

        Assert.Equal([(docB, "User", "Hỏi B"), (docB, "AI", "Đáp mới")], chat.Appended);
    }

    [Fact]
    public async Task SendingNewQuestion_CancelsPreviousRequest_AndOnlyNewTurnIsPersisted()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var tokens = new List<CancellationToken>();
        var (vm, chat) = BuildVm(ct =>
        {
            tokens.Add(ct);
            return tokens.Count == 1 ? BlockingStream(started, ct) : ShortStream("Đáp mới");
        });
        string docA = OpenDocument(vm, "docA");
        vm.Chat.Input = "Câu một";
        var first = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);
        vm.Chat.Input = "Câu hai";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null).WaitAsync(Timeout);

        await first.WaitAsync(Timeout);
        Assert.True(tokens[0].IsCancellationRequested);
        Assert.DoesNotContain(_sentMessages[1], m => m.Text.Contains("Câu một", StringComparison.Ordinal));
        Assert.Equal([(docA, "User", "Câu hai"), (docA, "AI", "Đáp mới")], chat.Appended);
        Assert.DoesNotContain(vm.Chat.Messages, m => m.Content.Contains("lỗi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendingNewQuestion_WhileOldOneIsStillCancelling_KeepsNewUserTurnInLlmHistory()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var (vm, _) = BuildVm(ct => ++calls == 1 ? SlowToCancelStream(started, ct) : ShortStream("Đáp mới"));
        OpenDocument(vm, "docA");
        vm.Chat.Input = "Câu một";
        var first = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);
        vm.Chat.Input = "Câu hai";
        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null).WaitAsync(Timeout);
        await first.WaitAsync(Timeout);
        vm.Chat.Input = "Câu ba";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null).WaitAsync(Timeout);

        Assert.DoesNotContain(_sentMessages[1], m => m.Text.Contains("Câu một", StringComparison.Ordinal));
        var thirdCall = _sentMessages[2];
        Assert.Contains(thirdCall, m => m.Role == ChatRole.User && m.Text.Contains("Câu hai", StringComparison.Ordinal));
        Assert.DoesNotContain(thirdCall, m => m.Text.Contains("Câu một", StringComparison.Ordinal));
        Assert.DoesNotContain(thirdCall, m => m.Text.Contains("Đáp cũ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledRequest_WhoseStreamEndsNormally_IsNotPersistedNorRemembered()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var (vm, chat) = BuildVm(ct => ++calls == 1 ? TokenIgnoringStream(started, release.Task) : ShortStream("Đáp mới"));
        string docA = OpenDocument(vm, "docA");
        vm.Chat.Input = "Câu một";
        var send = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);
        vm.Chat.CancelPendingRequest();
        release.SetResult();
        await send.WaitAsync(Timeout);
        vm.Chat.Input = "Câu hai";

        await vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null).WaitAsync(Timeout);

        Assert.Equal([(docA, "User", "Câu hai"), (docA, "AI", "Đáp mới")], chat.Appended);
        var secondCall = _sentMessages[1];
        Assert.DoesNotContain(secondCall, m => m.Text.Contains("Đáp cũ", StringComparison.Ordinal));
        Assert.DoesNotContain(secondCall, m => m.Text.Contains("Câu một", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CancelledRequest_WhoseStreamFailsWithNonCancellationError_ShowsNoErrorAndIsNotPersisted()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, chat) = BuildVm(_ => FailsAfterReleaseStream(started, release.Task));
        OpenDocument(vm, "docA");
        vm.Chat.Input = "Câu một";
        var send = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);

        vm.Chat.CancelPendingRequest();
        release.SetResult();
        await send.WaitAsync(Timeout);

        Assert.Empty(chat.Appended);
        Assert.DoesNotContain(vm.Chat.Messages, m => m.Content.Contains("lỗi", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task SendCommand_StaysExecutableWhileRequestRuns_SoNewQuestionCanReplaceIt()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (vm, _) = BuildVm(ct => BlockingStream(started, ct));
        OpenDocument(vm, "docA");
        vm.Chat.Input = "Câu một";
        var send = vm.Chat.SendMessageCommand.ExecuteAsync(parameter: null);
        await started.Task.WaitAsync(Timeout);

        Assert.True(vm.Chat.SendMessageCommand.CanExecute(parameter: null));

        vm.Chat.CancelPendingRequest();
        await send.WaitAsync(Timeout);
    }
}
