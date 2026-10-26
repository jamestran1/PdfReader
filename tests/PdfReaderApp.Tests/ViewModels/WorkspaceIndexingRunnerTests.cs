using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class WorkspaceIndexingRunnerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static WorkspaceDocument Doc(string id) => new(id, $"Sách {id}", $"C:\\lib\\{id}.pdf");

    // Mặc định mỗi tài liệu lập xong đạt Complete; test cần kết cục khác dùng CreateWithOutcome.
    private static WorkspaceIndexingRunner Create(Func<WorkspaceDocument, CancellationToken, Task> indexDocument)
        => new(
            async (document, ct) =>
            {
                await indexDocument(document, ct);
                return DocumentIndexStatus.Complete;
            },
            postToUiThread: action => action());

    private static WorkspaceIndexingRunner CreateWithOutcome(DocumentIndexStatus outcome)
        => new((_, _) => Task.FromResult(outcome), postToUiThread: action => action());

    private static async Task Finished(WorkspaceIndexingRunner queue)
        => await (queue.BackgroundTask ?? Task.CompletedTask).WaitAsync(Timeout);

    [Fact]
    public async Task Enqueue_ShowsCountOfDocumentsBeingIndexedAndNotYetIncluded()
    {
        var gate = new TaskCompletionSource();
        using var queue = Create(async (_, _) => await gate.Task);

        queue.Enqueue([Doc("a"), Doc("b")]);

        Assert.Equal("Đang lập chỉ mục 2 tài liệu, câu trả lời chưa gồm các tài liệu này.", queue.StatusText);
        gate.SetResult();
        await Finished(queue);
    }

    [Fact]
    public async Task Enqueue_IndexesDocumentsOneAtATimeInOrder()
    {
        var order = new List<string>();
        int running = 0;
        int maxRunning = 0;
        using var queue = Create(async (document, _) =>
        {
            maxRunning = Math.Max(maxRunning, Interlocked.Increment(ref running));
            await Task.Delay(10, CancellationToken.None);
            order.Add(document.DocumentId);
            Interlocked.Decrement(ref running);
        });

        queue.Enqueue([Doc("a"), Doc("b"), Doc("c")]);
        await Finished(queue);

        Assert.Equal(["a", "b", "c"], order, StringComparer.Ordinal);
        Assert.Equal(1, maxRunning);
    }

    [Fact]
    public async Task Enqueue_WhenAllFinish_ShowsDoneMessage()
    {
        using var queue = Create((_, _) => Task.CompletedTask);

        queue.Enqueue([Doc("a"), Doc("b")]);
        await Finished(queue);

        Assert.Equal("Đã lập chỉ mục xong 2 tài liệu. Hỏi lại để câu trả lời gồm cả các tài liệu này.", queue.StatusText);
    }

    [Fact]
    public async Task Enqueue_RemainingCountDropsAsEachDocumentFinishes()
    {
        var first = new TaskCompletionSource();
        var second = new TaskCompletionSource();
        var started = new List<string>();
        using var queue = Create(async (document, _) =>
        {
            started.Add(document.DocumentId);
            await (document.DocumentId == "a" ? first : second).Task;
        });

        queue.Enqueue([Doc("a"), Doc("b")]);
        first.SetResult();
        await WaitUntilAsync(() => started.Contains("b", StringComparer.Ordinal));
        await WaitUntilAsync(() => queue.StatusText.Contains("1 tài liệu", StringComparison.Ordinal));

        Assert.Equal("Đang lập chỉ mục 1 tài liệu, câu trả lời chưa gồm các tài liệu này.", queue.StatusText);
        second.SetResult();
        await Finished(queue);
    }

    [Fact]
    public async Task Enqueue_DocumentAlreadyQueued_IsNotIndexedTwice()
    {
        var gate = new TaskCompletionSource();
        var calls = new List<string>();
        using var queue = Create(async (document, _) =>
        {
            calls.Add(document.DocumentId);
            await gate.Task;
        });

        queue.Enqueue([Doc("a")]);
        queue.Enqueue([Doc("a"), Doc("b")]);
        gate.SetResult();
        await Finished(queue);

        Assert.Equal(["a", "b"], calls, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Enqueue_OneDocumentFails_OthersStillIndexedAndFailureReported()
    {
        var indexed = new List<string>();
        using var queue = Create((document, _) =>
        {
            if (document.DocumentId == "a") throw new InvalidOperationException("hỏng");
            indexed.Add(document.DocumentId);
            return Task.CompletedTask;
        });

        queue.Enqueue([Doc("a"), Doc("b")]);
        await Finished(queue);

        Assert.Equal(["b"], indexed, StringComparer.Ordinal);
        Assert.Contains("Không lập được chỉ mục cho 1 tài liệu", queue.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancel_StopsInFlightIndexingAndClearsStatus()
    {
        var cancelled = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        using var queue = Create(async (_, ct) =>
        {
            started.SetResult();
            try { await Task.Delay(System.Threading.Timeout.Infinite, ct); }
            catch (OperationCanceledException)
            {
                cancelled.SetResult();
                throw;
            }
        });

        queue.Enqueue([Doc("a"), Doc("b")]);
        await started.Task.WaitAsync(Timeout);
        queue.Cancel();
        await cancelled.Task.WaitAsync(Timeout);
        await Finished(queue);

        Assert.Equal(string.Empty, queue.StatusText);
    }

    [Fact]
    public async Task Cancel_ThenEnqueueSameDocument_IndexesItAgain()
    {
        var calls = 0;
        var started = new TaskCompletionSource();
        using var queue = Create(async (_, ct) =>
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                started.SetResult();
                await Task.Delay(System.Threading.Timeout.Infinite, ct);
            }
        });

        queue.Enqueue([Doc("a")]);
        await started.Task.WaitAsync(Timeout);
        queue.Cancel();
        queue.Enqueue([Doc("a")]);
        await Finished(queue);

        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(DocumentIndexStatus.TextOnly)]
    [InlineData(DocumentIndexStatus.Partial)]
    [InlineData(DocumentIndexStatus.Empty)]
    [InlineData(DocumentIndexStatus.None)]
    public async Task Enqueue_DocumentDoesNotReachComplete_ReportsFailureNotDone(DocumentIndexStatus outcome)
    {
        using var queue = CreateWithOutcome(outcome);

        queue.Enqueue([Doc("a")]);
        await Finished(queue);

        Assert.Contains("Không lập được chỉ mục cho 1 tài liệu", queue.StatusText, StringComparison.Ordinal);
        Assert.DoesNotContain("Đã lập chỉ mục xong", queue.StatusText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enqueue_AnotherRunIsIndexingTheDocument_CountsNeitherDoneNorFailed()
    {
        using var queue = CreateWithOutcome(DocumentIndexStatus.Indexing);

        queue.Enqueue([Doc("a")]);
        await Finished(queue);

        Assert.Equal(string.Empty, queue.StatusText);
    }

    [Fact]
    public async Task Enqueue_AfterDispose_IsIgnored()
    {
        int calls = 0;
        var queue = Create((_, _) =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        });
        queue.Dispose();

        queue.Enqueue([Doc("a")]);
        await Finished(queue);

        Assert.Equal(0, calls);
        Assert.Equal(string.Empty, queue.StatusText);
    }

    [Fact]
    public async Task Dispose_WhileIndexing_LateFinishDoesNotChangeStatus()
    {
        var gate = new TaskCompletionSource();
        var started = new TaskCompletionSource();
        var queue = Create(async (_, _) =>
        {
            started.SetResult();
            await gate.Task;
        });
        queue.Enqueue([Doc("a")]);
        await started.Task.WaitAsync(Timeout);
        string before = queue.StatusText;

        queue.Dispose();
        gate.SetResult();
        await Finished(queue);

        Assert.Equal(before, queue.StatusText);
    }

    [Fact]
    public void SetExternallyIndexing_ShowsTheCountAndZeroClearsIt()
    {
        using var queue = Create((_, _) => Task.CompletedTask);

        queue.SetExternallyIndexing(2);
        Assert.Equal("Đang lập chỉ mục 2 tài liệu, câu trả lời chưa gồm các tài liệu này.", queue.StatusText);

        queue.SetExternallyIndexing(0);
        Assert.Equal(string.Empty, queue.StatusText);
    }

    [Fact]
    public async Task SetExternallyIndexing_AddsToTheQueuedRemainingCount()
    {
        var gate = new TaskCompletionSource();
        using var queue = Create(async (_, _) => await gate.Task);

        queue.SetExternallyIndexing(1);
        queue.Enqueue([Doc("a")]);

        Assert.Equal("Đang lập chỉ mục 2 tài liệu, câu trả lời chưa gồm các tài liệu này.", queue.StatusText);
        gate.SetResult();
        await Finished(queue);
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
}
