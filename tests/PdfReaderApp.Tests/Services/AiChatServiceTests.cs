using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiChatServiceTests
{
    // ---- Fakes ----
    private sealed class FakeSettings : ISettingsService
    {
        private string? _key;
        public FakeSettings(string? key) => _key = key;
        public string? GetApiKey() => _key;
        public void SaveApiKey(string apiKey) => _key = apiKey;
        public bool HasApiKey() => !string.IsNullOrEmpty(_key);
    }

    private sealed class FakeChatClient : IChatClient
    {
        private readonly string[] _tokens;
        private readonly bool _throwAfterTokens;
        public List<ChatMessage> LastMessages { get; private set; } = new();

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
        public IChatClient Create(string apiKey) => _client;
    }

    /// <summary>Factory that returns clients from a queue, one per Create call.</summary>
    private sealed class SequentialFactory : IChatClientFactory
    {
        private readonly Queue<IChatClient> _clients;
        public SequentialFactory(params IChatClient[] clients) => _clients = new Queue<IChatClient>(clients);
        public IChatClient Create(string apiKey) => _clients.Dequeue();
    }

    /// <summary>Throws on the first MoveNextAsync — simulates a fatal pre-token error.</summary>
    private sealed class ThrowOnFirstMoveClient : IChatClient
    {
        public List<ChatMessage> LastMessages { get; private set; } = new();

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

    [Fact]
    public async Task AskStreaming_YieldsTokensInOrder()
    {
        var client = new FakeChatClient(new[] { "Xin ", "chào", "!" });
        var svc = new AiChatService(new FakeSettings("sk-x"), new FakeFactory(client));

        var tokens = await Collect(svc.AskStreamingAsync("hi", "ngữ cảnh"));

        Assert.Equal(new[] { "Xin ", "chào", "!" }, tokens);
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
        mutSettings.SaveApiKey("sk-second");

        // Act 2: successful call.
        await Collect(svc.AskStreamingAsync("Câu hỏi hợp lệ", "ctx"));

        // Assert: history sent to goodClient must NOT contain the rolled-back failed question.
        var texts = goodClient.LastMessages.Select(m => m.Text ?? "").ToList();
        Assert.DoesNotContain(texts, t => t.Contains("Câu hỏi lỗi"));
        Assert.Contains(texts, t => t.Contains("Câu hỏi hợp lệ"));
    }
}
