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

    private sealed class FakeChatClientFactory(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => new FakeChatClient(stream);
    }

    private sealed class FakeChatClient(Func<CancellationToken, IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<AiMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<AiMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => stream(cancellationToken);

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeChatHistoryStore : IChatHistoryStore
    {
        public readonly List<(string owner, string role, string content)> Appended = [];
        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix)
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
        public void Dispose() { }
    }

    private sealed class FakeDocumentIndex : IDocumentIndex
    {
        public void EnsureSchema() { }
        public DocumentIndexStatus GetStatus(string documentId, string embeddingModel) => DocumentIndexStatus.None;
        public void DeleteDocument(string documentId) { }
        public IReadOnlyList<long> WriteChunks(string documentId, string? filePath, int pageCount, IReadOnlyList<Chunk> chunks) => [];
        public void WriteEmbeddings(IReadOnlyList<(long ChunkId, float[] Vector)> embeddings) { }
        public void SetStatus(string documentId, DocumentIndexStatus status, string embeddingModel) { }
        public IReadOnlyList<SearchResult> SearchText(string documentId, string query, int limit = 50) => [];
        public IReadOnlyList<Chunk> RetrieveRelevant(string documentId, float[] queryVector, int k = 5) => [];
        public void Dispose() { }
    }

    private sealed class ThrowingEmbeddingFactory : IEmbeddingGeneratorFactory
    {
        public IEmbeddingGenerator<string, Embedding<float>> Create(AiClientOptions options)
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
            new FakeChatClientFactory(stream),
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
        return vm.CurrentDocumentId!;
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
        Assert.Equal([(docA, "User", "Câu hai"), (docA, "AI", "Đáp mới")], chat.Appended);
        Assert.DoesNotContain(vm.Chat.Messages, m => m.Content.Contains("lỗi", StringComparison.OrdinalIgnoreCase));
    }
}
