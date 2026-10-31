using System.Net.Http;
using Microsoft.Extensions.AI;
using AiMessage = Microsoft.Extensions.AI.ChatMessage;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class AiCompositionGeneratorTests
{
    private sealed class FakeSettings(string? apiKey = "key") : ISettingsService
    {
        public string? GetApiKey(AiProvider provider) => apiKey;
        public void SaveApiKey(AiProvider provider, string key) { }
        public bool HasApiKey(AiProvider provider) => apiKey is not null;
        public AppTheme GetThemePreference() => AppTheme.Light;
        public void SaveThemePreference(AppTheme theme) { }
        public AiModelSettings GetModelSettings() => AiModelSettings.Default;
        public void SaveModelSettings(AiModelSettings settings) { }
    }

    private sealed class FakeFactory(Func<IEnumerable<AiMessage>, CancellationToken, Task<string>> respond) : IChatClientFactory
    {
        public IChatClient Create(AiClientOptions options) => new FakeClient(respond);
    }

    private sealed class FakeClient(Func<IEnumerable<AiMessage>, CancellationToken, Task<string>> respond) : IChatClient
    {
        public async Task<ChatResponse> GetResponseAsync(IEnumerable<AiMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => new(new AiMessage(ChatRole.Assistant, await respond(messages, cancellationToken)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<AiMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
        public void Dispose() { }
    }

    private static readonly CompositionContext Context = new("[1]\nGhi chú: x", ["n1"]);

    [Fact]
    public async Task GenerateAsync_SendsNoteContextAsUserTurn_AndSplitsTitleFromContent()
    {
        List<AiMessage> sent = [];
        var generator = new AiCompositionGenerator(new FakeSettings(), new FakeFactory((msgs, _) =>
        {
            sent.AddRange(msgs);
            return Task.FromResult("# Tóm tắt về x\n\nNội dung chính [1].");
        }));

        var result = await generator.GenerateAsync(CompositionKind.Summary, Context, CancellationToken.None);

        Assert.Equal("Tóm tắt về x", result.Title);
        Assert.Equal("Nội dung chính [1].", result.Content);
        Assert.Equal(AiModelSettings.Default.ChatModel, result.ChatModel);
        Assert.Equal(ChatRole.System, sent[0].Role);
        Assert.Equal(ChatRole.User, sent[^1].Role);
        Assert.Contains("[1]\nGhi chú: x", sent[^1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_WithoutApiKey_ThrowsUnauthorized()
    {
        var generator = new AiCompositionGenerator(new FakeSettings(apiKey: null), new FakeFactory((_, _) => Task.FromResult("x")));

        var ex = await Assert.ThrowsAsync<AiChatException>(
            () => generator.GenerateAsync(CompositionKind.Summary, Context, CancellationToken.None));

        Assert.Equal(AiChatError.Unauthorized, ex.Error);
    }

    [Fact]
    public async Task GenerateAsync_ProviderFailure_IsWrappedAsAiChatException()
    {
        var generator = new AiCompositionGenerator(new FakeSettings(), new FakeFactory((_, _) => throw new HttpRequestException("mạng đứt")));

        var ex = await Assert.ThrowsAsync<AiChatException>(
            () => generator.GenerateAsync(CompositionKind.Summary, Context, CancellationToken.None));

        Assert.Equal(AiChatError.Network, ex.Error);
    }

    [Theory]
    [InlineData("# Tiêu đề\n\nThân", "Tiêu đề", "Thân")]
    [InlineData("**Tiêu đề đậm**\nThân một\nThân hai", "Tiêu đề đậm", "Thân một\nThân hai")]
    [InlineData("Chỉ một dòng", "Tóm tắt", "Chỉ một dòng")]
    [InlineData("\n\n## Có dòng trống đầu\r\n\r\nThân", "Có dòng trống đầu", "Thân")]
    public void ParseResponse_SplitsFirstLineAsTitle(string raw, string title, string content)
    {
        var parsed = AiCompositionGenerator.ParseResponse(raw, fallbackTitle: "Tóm tắt");

        Assert.Equal(title, parsed.Title);
        Assert.Equal(content, parsed.Content);
    }

    [Theory]
    [InlineData(CompositionKind.Summary, "Tóm tắt", "tóm tắt")]
    [InlineData(CompositionKind.Outline, "Outline", "outline")]
    [InlineData(CompositionKind.Draft, "Soạn thảo", "bản thảo")]
    public async Task GenerateAsync_EachKindHasItsOwnInstruction_AndFallbackTitle(CompositionKind kind, string fallbackTitle, string keyword)
    {
        string system = string.Empty;
        var generator = new AiCompositionGenerator(new FakeSettings(), new FakeFactory((msgs, _) =>
        {
            system = msgs.First(m => m.Role == ChatRole.System).Text;
            return Task.FromResult("Chỉ một dòng");
        }));

        var result = await generator.GenerateAsync(kind, Context, CancellationToken.None);

        Assert.Equal(fallbackTitle, result.Title);
        Assert.Contains(keyword, system, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[n]", system, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GenerateAsync_KindsUseDistinctSystemPrompts()
    {
        HashSet<string> prompts = [];
        var generator = new AiCompositionGenerator(new FakeSettings(), new FakeFactory((msgs, _) =>
        {
            prompts.Add(msgs.First(m => m.Role == ChatRole.System).Text);
            return Task.FromResult("T\n\nC");
        }));

        foreach (var kind in new[] { CompositionKind.Summary, CompositionKind.Outline, CompositionKind.Draft })
            await generator.GenerateAsync(kind, Context, CancellationToken.None);

        Assert.Equal(3, prompts.Count);
    }

    [Fact]
    public async Task GenerateAsync_AdditionalRequest_IsSentWithTheNotes_AndAbsentWhenNull()
    {
        List<string> userTurns = [];
        var generator = new AiCompositionGenerator(new FakeSettings(), new FakeFactory((msgs, _) =>
        {
            userTurns.Add(msgs.Last().Text);
            return Task.FromResult("T\n\nC");
        }));

        await generator.GenerateAsync(CompositionKind.Outline, Context with { AdditionalRequest = "viết bằng tiếng Anh" }, CancellationToken.None);
        await generator.GenerateAsync(CompositionKind.Outline, Context, CancellationToken.None);

        Assert.Contains("viết bằng tiếng Anh", userTurns[0], StringComparison.Ordinal);
        Assert.Contains("[1]\nGhi chú: x", userTurns[0], StringComparison.Ordinal);
        Assert.DoesNotContain("Yêu cầu thêm", userTurns[1], StringComparison.Ordinal);
    }
}
