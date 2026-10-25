using Microsoft.Extensions.AI;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class OwningChatClientTests
{
    private sealed class DisposeProbe : IChatClient, IDisposable
    {
        public int Disposed { get; private set; }

        public void Dispose() => Disposed++;

        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
            ChatOptions? options = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;
    }

    [Fact]
    public void Dispose_DisposesBothWrappedClientAndOwnedResource()
    {
        var inner = new DisposeProbe();
        var owned = new DisposeProbe();

        new OwningChatClient(inner, owned).Dispose();

        Assert.Equal(1, inner.Disposed);
        Assert.Equal(1, owned.Disposed);
    }
}
