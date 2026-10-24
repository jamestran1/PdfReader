using System.IO;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
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

    private const string NotConfigured =
        "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key.";

    private sealed class FakeSettings(bool hasKey) : ISettingsService
    {
        public string? GetApiKey() => hasKey ? "key" : null;
        public void SaveApiKey(string apiKey) { }
        public bool HasApiKey() => hasKey;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
    }

    private sealed class FakeChatClientFactory(Func<IAsyncEnumerable<ChatResponseUpdate>> stream) : IChatClientFactory
    {
        public IChatClient Create(string apiKey) => new FakeChatClient(stream);
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
        public void Append(string documentId, string role, string content, long createdAtUnix)
            => Appended.Add((documentId, role, content));
        public IReadOnlyList<ChatHistoryEntry> GetAll(string documentId) => [];
        public void DeleteForDocument(string documentId) { }
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
            new OpenAiEmbeddingGeneratorFactory(),
            chatHistory: chat,
            workspaceStore: wsStore ?? new FakeWorkspaceStore());
        _viewModels.Add(vm);
        return vm;
    }

    // MainViewModel.Dispose đóng SqliteDocumentIndex, nên chỉ còn việc xoá file .db tạm.
    public void Dispose()
    {
        foreach (var vm in _viewModels) vm.Dispose();
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
        vm.ChatInput = "   ";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Single(vm.ChatMessages); // chỉ còn lời chào
        Assert.Equal("   ", vm.ChatInput);
    }

    [Fact]
    public async Task SendMessage_NotConfigured_AddsUserAndNotConfiguredAiMessage_AndClearsInput()
    {
        var vm = BuildVm(hasKey: false, () => Tokens("x"), new FakeChatHistoryStore());
        vm.ChatInput = "Xin chào";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(string.Empty, vm.ChatInput);
        Assert.Equal(3, vm.ChatMessages.Count);
        Assert.Equal("User", vm.ChatMessages[1].Role);
        Assert.Equal("Xin chào", vm.ChatMessages[1].Content);
        Assert.Equal("AI", vm.ChatMessages[2].Role);
        Assert.Equal(NotConfigured, vm.ChatMessages[2].Content);
    }

    [Fact]
    public async Task SendMessage_Configured_StreamsTokensIntoAiMessage()
    {
        var vm = BuildVm(hasKey: true, () => Tokens("Xin ", "chào"), new FakeChatHistoryStore());
        vm.ChatInput = "Hỏi";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(3, vm.ChatMessages.Count);
        Assert.Equal("AI", vm.ChatMessages[2].Role);
        Assert.Equal("Xin chào", vm.ChatMessages[2].Content);
    }

    [Fact]
    public async Task SendMessage_NetworkFailure_ShowsMappedNetworkError()
    {
        var vm = BuildVm(hasKey: true, () => Throwing(new System.Net.Http.HttpRequestException("down")), new FakeChatHistoryStore());
        vm.ChatInput = "Hỏi";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal("Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.", vm.ChatMessages[2].Content);
    }

    [Fact]
    public async Task SendMessage_CanBeSentAgainAfterFailure()
    {
        var vm = BuildVm(hasKey: true, () => Throwing(new System.Net.Http.HttpRequestException("down")), new FakeChatHistoryStore());
        vm.ChatInput = "Một";
        await vm.SendMessageCommand.ExecuteAsync(parameter: null);
        vm.ChatInput = "Hai";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

        Assert.Equal(5, vm.ChatMessages.Count);
    }

    [Fact]
    public async Task SendMessage_NoDocumentNorWorkspace_DoesNotPersistTurn()
    {
        var chat = new FakeChatHistoryStore();
        var vm = BuildVm(hasKey: true, () => Tokens("Đáp"), chat);
        vm.ChatInput = "Hỏi";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

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
        vm.ChatInput = "Hỏi";

        await vm.SendMessageCommand.ExecuteAsync(parameter: null);

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
        Assert.Single(vm.ChatMessages); // lời chào
        Assert.Equal("AI", vm.ChatMessages[0].Role);
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
}
