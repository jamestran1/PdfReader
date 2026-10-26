using System.Net.Http;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiChatServiceTests
{
    // ---- Fakes ----
    private sealed class FakeSettings : ISettingsService
    {
        private string? _key;
        public FakeSettings(string? key) => _key = key;
        public string? GetApiKey(AiProvider provider) => _key;
        public void SaveApiKey(AiProvider provider, string apiKey) => _key = apiKey;
        public bool HasApiKey(AiProvider provider) => !string.IsNullOrEmpty(_key);
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string[] _tokens;
        private readonly bool _throwAfterTokens;
        public List<ChatMessage> LastMessages { get; private set; } = [];

        public FakeChatClient(string[] tokens, bool throwAfterTokens = false)
        {
            _tokens = tokens;
            _throwAfterTokens = throwAfterTokens;
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            foreach (var t in _tokens)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, t);
            }
            if (_throwAfterTokens)
                throw new InvalidOperationException("stream broke");
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private sealed class FakeFactory : IChatClientFactory
    {
        private readonly IChatClient _client;
        public FakeFactory(IChatClient client) => _client = client;
        public IChatClient Create(AiClientOptions options) => _client;
    }

    /// <summary>Factory that returns clients from a queue, one per Create call.</summary>
    private sealed class SequentialFactory : IChatClientFactory
    {
        private readonly Queue<IChatClient> _clients;
        public SequentialFactory(params IChatClient[] clients) => _clients = new Queue<IChatClient>(clients);
        public IChatClient Create(AiClientOptions options) => _clients.Dequeue();
    }

    /// <summary>Throws on the first MoveNextAsync — simulates a fatal pre-token error.</summary>
    private sealed class ThrowOnFirstMoveClient : IChatClient
    {
        public List<ChatMessage> LastMessages { get; private set; } = [];

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            await Task.Yield();
            throw new InvalidOperationException("fatal before first token");
#pragma warning disable CS0162
            yield break; // makes this an async iterator
#pragma warning restore CS0162
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static async Task<List<string>> Collect(IAsyncEnumerable<string> stream)
    {
        var list = new List<string>();
        await foreach (var s in stream) list.Add(s);
        return list;
    }

    // ---- Tests ----

    private sealed class ProviderSettings(AiModelSettings models, params AiProvider[] withKey) : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => withKey.Contains(provider) ? $"key-{provider}" : null;
        public void SaveApiKey(AiProvider provider, string apiKey) { }
        public bool HasApiKey(AiProvider provider) => withKey.Contains(provider);
        public AiModelSettings GetModelSettings() => models;
        public void SaveModelSettings(AiModelSettings settings) { }
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
    }

    private sealed class CapturingFactory(IChatClient client) : IChatClientFactory
    {
        public AiClientOptions? Last { get; private set; }

        public IChatClient Create(AiClientOptions options)
        {
            Last = options;
            return client;
        }
    }

    [Fact]
    public void IsConfigured_ChecksKeyOfChatProvider()
    {
        var models = new AiModelSettings(AiProvider.Claude, "claude-x", AiProvider.OpenAi, "e");

        Assert.False(new AiChatService(new ProviderSettings(models, AiProvider.OpenAi), new FakeFactory(new FakeChatClient([]))).IsConfigured);
        Assert.True(new AiChatService(new ProviderSettings(models, AiProvider.Claude), new FakeFactory(new FakeChatClient([]))).IsConfigured);
    }

    private sealed class UnsupportedFactory : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => throw new NotSupportedException("chưa hỗ trợ");
    }

    [Fact]
    public async Task AskStreaming_ProviderNotSupported_ThrowsUnsupportedProviderError()
    {
        // Claude đã được hỗ trợ (#104); hiện chỉ Gemini còn chưa có client chat (#105).
        var models = new AiModelSettings(AiProvider.Gemini, "gemini-x", AiProvider.OpenAi, "e");
        var svc = new AiChatService(new ProviderSettings(models, AiProvider.Gemini), new UnsupportedFactory());

        var ex = await Assert.ThrowsAsync<AiChatException>(() => Collect(svc.AskStreamingAsync("hi", "ctx")));

        Assert.Equal(AiChatError.UnsupportedProvider, ex.Error);
        Assert.IsType<NotSupportedException>(ex.InnerException);
    }

    [Fact]
    public async Task AskStreaming_BuildsClientFromChatProviderModelAndKey()
    {
        var models = new AiModelSettings(AiProvider.Gemini, "gemini-x", AiProvider.OpenAi, "e");
        var factory = new CapturingFactory(new FakeChatClient(["ok"]));
        var svc = new AiChatService(new ProviderSettings(models, AiProvider.Gemini), factory);

        await Collect(svc.AskStreamingAsync("hi", "ctx"));

        Assert.Equal(new AiClientOptions(AiProvider.Gemini, "gemini-x", "key-Gemini"), factory.Last);
    }

    [Fact]
    public async Task AskStreaming_YieldsTokensInOrder()
    {
        var client = new FakeChatClient(new[] { "Xin ", "chào", "!" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ngữ cảnh"));

        Assert.Equal(new[] { "Xin ", "chào", "!" }, tokens, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AskStreaming_SendsSystemContextAndQuestion()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Câu hỏi?", "Nội dung tài liệu ABC"));

        var roles = client.LastMessages.Select(m => m.Role).ToList();
        Assert.Equal(ChatRole.System, roles.First());
        var joined = string.Join("\n", client.LastMessages.Select(m => m.Text));
        Assert.Contains("Nội dung tài liệu ABC", joined);
        Assert.Equal(ChatRole.User, client.LastMessages.Last().Role);
        Assert.Contains("Câu hỏi?", client.LastMessages.Last().Text);
    }

    [Fact]
    public async Task AskStreaming_SecondTurn_IncludesFirstTurnHistory()
    {
        var client = new FakeChatClient(new[] { "Trả lời 1" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var texts = client.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("Hỏi 1"));
        Assert.Contains(texts, t => t.Contains("Trả lời 1"));
        Assert.Contains(texts, t => t.Contains("Hỏi 2"));
    }

    [Fact]
    public async Task AskStreaming_MidStreamError_AppendsSentinelAndStops()
    {
        var client = new FakeChatClient(new[] { "phần ", "đầu" }, throwAfterTokens: true);
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ctx"));

        Assert.Equal("phần ", tokens[0]);
        Assert.Equal("đầu", tokens[1]);
        Assert.Equal(" ...[lỗi: phản hồi bị gián đoạn]", tokens[2]);
    }

    [Fact]
    public async Task AskStreaming_NoApiKey_ThrowsUnauthorized()
    {
        var client = new FakeChatClient(new[] { "x" });
        var svc = new AiChatService(new FakeSettings(null), new FakeFactory(client));

        var ex = await Assert.ThrowsAsync<AiChatException>(
            async () => await Collect(svc.AskStreamingAsync("hi", "ctx")));
        Assert.Equal(AiChatError.Unauthorized, ex.Error);
    }

    [Fact]
    public async Task ResetConversation_DropsHistory()
    {
        var client = new FakeChatClient(new[] { "a" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        svc.ResetConversation();
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var texts = client.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Hỏi 1"));
        Assert.Contains(texts, t => t.Contains("Hỏi 2"));
    }

    [Fact]
    public async Task AskStreaming_FatalBeforeFirstToken_RollsBackUserTurn()
    {
        // Arrange: first call uses a client that throws before yielding any token.
        // Second call uses a good client; key rotation forces _client rebuild so
        // SequentialFactory hands out the good client on the second Create().
        var mutSettings = new FakeSettings("sk-first");
        var throwingClient = new ThrowOnFirstMoveClient();
        var goodClient = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(mutSettings, new SequentialFactory(throwingClient, goodClient));

        // Act 1: fatal error before any token -- must throw AiChatException.
        await Assert.ThrowsAsync<AiChatException>(
            async () => await Collect(svc.AskStreamingAsync("Câu hỏi lỗi", "ctx")));

        // Rotate key to force _client rebuild on next call (SequentialFactory -> goodClient).
        mutSettings.SaveApiKey(AiProvider.OpenAi, "sk-second");

        // Act 2: successful call.
        await Collect(svc.AskStreamingAsync("Câu hỏi hợp lệ", "ctx"));

        // Assert: history sent to goodClient must NOT contain the rolled-back failed question.
        var texts = goodClient.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Câu hỏi lỗi"));
        Assert.Contains(texts, t => t.Contains("Câu hỏi hợp lệ"));
    }

    [Fact]
    public async Task SeedHistory_IncludesPriorTurnsBeforeNewQuestion()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        svc.SeedHistory(new[]
        {
            ("User", "Hỏi cũ"),
            ("AI", "Đáp cũ"),
        });
        await Collect(svc.AskStreamingAsync("Hỏi mới", "ctx"));

        var msgs = client.LastMessages;
        // System đứng đầu, rồi 2 lượt cũ, rồi câu hỏi mới ở cuối.
        Assert.Equal(ChatRole.System, msgs.First().Role);
        var texts = msgs.Select(m => m.Text ?? "").ToList();
        Assert.Contains(texts, t => t.Contains("Hỏi cũ"));
        Assert.Contains(texts, t => t.Contains("Đáp cũ"));
        Assert.Equal(ChatRole.Assistant, msgs[2].Role); // lượt "AI" cũ map sang Assistant
        Assert.Contains("Hỏi mới", msgs.Last().Text);
        Assert.Equal(ChatRole.User, msgs.Last().Role);
        // Lịch sử cũ là nội dung sạch, không nhúng khối ngữ cảnh tài liệu.
        Assert.DoesNotContain("Ngữ cảnh tài liệu", msgs[1].Text);
    }

    /// <summary>Client cấu hình được: ném đồng bộ khi gọi, hoặc ném giữa chừng sau các token, và đếm Dispose.</summary>
    private sealed class ScriptedClient : IChatClient
    {
        private readonly string[] _tokens;
        private readonly Exception? _throwAfterTokens;
        private readonly Exception? _throwOnCall;
        public List<ChatMessage> LastMessages { get; private set; } = [];
        public int DisposeCount { get; private set; }

        public ScriptedClient(string[] tokens, Exception? throwAfterTokens = null, Exception? throwOnCall = null)
        {
            _tokens = tokens;
            _throwAfterTokens = throwAfterTokens;
            _throwOnCall = throwOnCall;
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            LastMessages = messages.ToList();
            if (_throwOnCall != null) throw _throwOnCall;
            return Stream();
        }

        private async IAsyncEnumerable<ChatResponseUpdate> Stream()
        {
            foreach (var t in _tokens)
            {
                await Task.Yield();
                yield return new ChatResponseUpdate(ChatRole.Assistant, t);
            }
            if (_throwAfterTokens != null) throw _throwAfterTokens;
        }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() => DisposeCount++;
    }

    [Fact]
    public async Task AskStreaming_LongContext_IsTruncatedTo48000Chars()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("hỏi", new string('a', 60000)));

        string sent = client.LastMessages[^1].Text!;
        Assert.Equal(48000, sent.Count(c => c == 'a'));
    }

    [Fact]
    public async Task AskStreaming_EmptyTokens_AreSkipped()
    {
        var client = new FakeChatClient(new[] { "a", "", "b" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ctx"));

        Assert.Equal(new[] { "a", "b" }, tokens, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AskStreaming_Success_StoresAssistantReplyInHistory()
    {
        var client = new FakeChatClient(new[] { "Trả ", "lời" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var assistant = client.LastMessages.Where(m => m.Role == ChatRole.Assistant).ToList();
        Assert.Single(assistant);
        Assert.Equal("Trả lời", assistant[0].Text);
    }

    [Fact]
    public async Task AskStreaming_MidStreamError_KeepsPartialReplyWithSentinelInHistory()
    {
        var client = new FakeChatClient(new[] { "phần ", "đầu" }, throwAfterTokens: true);
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var assistant = client.LastMessages.Single(m => m.Role == ChatRole.Assistant);
        Assert.Equal("phần đầu" + AiChatService.InterruptedSentinel, assistant.Text);
    }

    [Fact]
    public async Task AskStreaming_MidStreamCancellation_AppendsSentinelInsteadOfThrowing()
    {
        var client = new ScriptedClient(new[] { "a" }, throwAfterTokens: new OperationCanceledException());
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ctx"));

        Assert.Equal(new[] { "a", AiChatService.InterruptedSentinel }, tokens, StringComparer.Ordinal);
    }

    [Fact]
    public async Task AskStreaming_FatalBeforeFirstToken_ThrowsClassifiedErrorWithInner()
    {
        var inner = new HttpRequestException("down");
        var client = new ScriptedClient(Array.Empty<string>(), throwAfterTokens: inner);
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var ex = await Assert.ThrowsAsync<AiChatException>(
            async () => await Collect(svc.AskStreamingAsync("hi", "ctx")));

        Assert.Equal(AiChatError.Network, ex.Error);
        Assert.Same(inner, ex.InnerException);
    }

    [Fact]
    public async Task AskStreaming_CallThrowsSynchronously_ThrowsClassifiedErrorAndRollsBackUserTurn()
    {
        var inner = new HttpRequestException("down");
        var failing = new ScriptedClient(Array.Empty<string>(), throwOnCall: inner);
        var good = new FakeChatClient(new[] { "ok" });
        var settings = new FakeSettings("sk-1");
        var svc = new AiChatService(settings, new SequentialFactory(failing, good));

        var ex = await Assert.ThrowsAsync<AiChatException>(
            async () => await Collect(svc.AskStreamingAsync("Câu hỏi lỗi", "ctx")));
        settings.SaveApiKey(AiProvider.OpenAi, "sk-2");
        await Collect(svc.AskStreamingAsync("Câu hỏi tốt", "ctx"));

        Assert.Equal(AiChatError.Network, ex.Error);
        Assert.Same(inner, ex.InnerException);
        Assert.DoesNotContain(good.LastMessages, m => (m.Text ?? "").Contains("Câu hỏi lỗi", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskStreaming_SameKey_ReusesClient()
    {
        var client = new ScriptedClient(new[] { "ok" });
        // Hàng đợi chỉ có một client: lần Create thứ hai sẽ ném.
        var svc = new AiChatService(new FakeSettings("sk-x"), new SequentialFactory(client));

        await Collect(svc.AskStreamingAsync("1", "ctx"));
        await Collect(svc.AskStreamingAsync("2", "ctx"));

        Assert.Equal(0, client.DisposeCount);
    }

    [Fact]
    public async Task AskStreaming_KeyChange_DisposesOldClientAndCreatesNew()
    {
        var first = new ScriptedClient(new[] { "a" });
        var second = new ScriptedClient(new[] { "b" });
        var settings = new FakeSettings("sk-1");
        var svc = new AiChatService(settings, new SequentialFactory(first, second));

        await Collect(svc.AskStreamingAsync("1", "ctx"));
        settings.SaveApiKey(AiProvider.OpenAi, "sk-2");
        var tokens = await Collect(svc.AskStreamingAsync("2", "ctx"));

        Assert.Equal(1, first.DisposeCount);
        Assert.Equal(new[] { "b" }, tokens, StringComparer.Ordinal);
    }

    [Fact]
    public async Task SeedHistory_Empty_LeavesOnlySystemPrompt()
    {
        var client = new FakeChatClient(new[] { "ok" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        await Collect(svc.AskStreamingAsync("Hỏi 1", "ctx"));
        svc.SeedHistory(System.Array.Empty<(string, string)>());
        await Collect(svc.AskStreamingAsync("Hỏi 2", "ctx"));

        var texts = client.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Hỏi 1"));
        Assert.Contains(texts, t => t.Contains("Hỏi 2"));
    }

    [Fact]
    public async Task AskStreamingAsync_HistoryOverBudget_SendsTrimmedHistory()
    {
        var client = new FakeChatClient(["ok"]);
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));
        string big = new('x', ChatHistoryTrimmer.DefaultMaxChars / 4);
        svc.SeedHistory([("User", "cũ-1 " + big), ("AI", "đáp-1 " + big),
                         ("User", "cũ-2 " + big), ("AI", "đáp-2 " + big),
                         ("User", "cũ-3 " + big), ("AI", "đáp-3 " + big)]);

        await foreach (var _ in svc.AskStreamingAsync("câu mới", "ngữ cảnh")) { }

        var sent = client.LastMessages;
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Contains("câu mới", sent[^1].Text);
        Assert.Contains("ngữ cảnh", sent[^1].Text);
        Assert.DoesNotContain(sent, m => m.Text.StartsWith("cũ-1", StringComparison.Ordinal));
        Assert.Contains(sent, m => m.Text.StartsWith("đáp-3", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AskStreamingAsync_HistoryWithinBudget_SendsEverything()
    {
        var client = new FakeChatClient(["ok"]);
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));
        svc.SeedHistory([("User", "hỏi cũ"), ("AI", "đáp cũ")]);

        await foreach (var _ in svc.AskStreamingAsync("câu mới", "")) { }

        Assert.Equal(4, client.LastMessages.Count);
    }
}
