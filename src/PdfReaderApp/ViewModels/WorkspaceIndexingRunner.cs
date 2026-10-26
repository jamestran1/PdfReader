using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Hàng đợi lập chỉ mục nền cho các Document của Workspace mà câu hỏi Toàn Workspace chưa dùng được.
/// Chạy tuần tự từng tài liệu, huỷ được (CS-13), và đưa tiến độ thành <see cref="StatusText"/> cho khung chat.</summary>
public sealed partial class WorkspaceIndexingRunner : ObservableObject, IDisposable
{
    private readonly Func<WorkspaceDocument, CancellationToken, Task> _indexDocumentAsync;
    private readonly Action<Action> _runOnUiThread;
    // Các trường dưới đây chỉ đọc/ghi trên luồng UI (Enqueue, Cancel và các lần cập nhật đều qua _runOnUiThread).
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private CancellationTokenSource _cts = new();
    // Tăng mỗi lần huỷ: báo cáo của lượt đã bị huỷ có thể tới muộn và không được ghi đè trạng thái mới.
    private int _generation;
    private int _remaining;
    private int _done;
    private int _failed;

    /// <summary>Thông báo cho người dùng; rỗng khi không có tài liệu nào đang chờ hoặc vừa lập xong.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Tác vụ nền gần nhất, null nếu chưa chạy. Để test chờ xác định thay vì ngủ.</summary>
    internal Task? BackgroundTask { get; private set; }

    /// <param name="indexDocumentAsync">Lập chỉ mục một tài liệu; ném <see cref="OperationCanceledException"/> khi bị huỷ.</param>
    /// <param name="runOnUiThread">Chạy thao tác trên luồng UI; mặc định dùng Dispatcher của ứng dụng,
    /// test truyền bản chạy tại chỗ vì không có Application.</param>
    public WorkspaceIndexingRunner(
        Func<WorkspaceDocument, CancellationToken, Task> indexDocumentAsync, Action<Action>? runOnUiThread = null)
    {
        _indexDocumentAsync = indexDocumentAsync;
        _runOnUiThread = runOnUiThread ?? RunOnApplicationDispatcher;
    }

    private static void RunOnApplicationDispatcher(Action action)
        => System.Windows.Application.Current?.Dispatcher.Invoke(action);

    /// <summary>Thêm tài liệu vào cuối hàng đợi; tài liệu đã nằm trong hàng đợi thì bỏ qua. Gọi trên luồng UI.</summary>
    public void Enqueue(IReadOnlyList<WorkspaceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var fresh = documents.Where(d => _queued.Add(d.DocumentId)).ToList();
        if (fresh.Count == 0) return;

        if (_remaining == 0)
        {
            _done = 0;
            _failed = 0;
        }

        _remaining += fresh.Count;
        StatusText = FormatStatus(_remaining, _done, _failed);

        int generation = _generation;
        CancellationToken ct = _cts.Token;
        Task previous = BackgroundTask ?? Task.CompletedTask;
        // Không truyền ct cho Task.Run: huỷ trước khi bắt đầu sẽ biến tác vụ thành Canceled và làm lượt sau ném khi chờ nó.
        BackgroundTask = Task.Run(() => RunAsync(previous, fresh, generation, ct));
    }

    /// <summary>Huỷ việc đang chạy và xoá hàng đợi, thông báo (đổi Workspace, đóng ứng dụng).</summary>
    public void Cancel()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _generation++;
        _queued.Clear();
        _remaining = 0;
        _done = 0;
        _failed = 0;
        StatusText = string.Empty;
    }

    private async Task RunAsync(Task previous, IReadOnlyList<WorkspaceDocument> documents, int generation, CancellationToken ct)
    {
        // Lượt trước (đã bị huỷ hoặc đang chạy) không bao giờ ném lỗi; chờ nó để giữ đúng thứ tự tuần tự.
        await previous.ConfigureAwait(false);
        try
        {
            foreach (var document in documents)
            {
                ct.ThrowIfCancellationRequested();
                bool succeeded = await TryIndexAsync(document, ct).ConfigureAwait(false);
                _runOnUiThread(() => OnDocumentFinished(document, succeeded, generation));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Huỷ có chủ đích (Cancel/Dispose): kết thúc êm, không báo lỗi.
        }
    }

    private async Task<bool> TryIndexAsync(WorkspaceDocument document, CancellationToken ct)
    {
        try
        {
            await _indexDocumentAsync(document, ct).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
#pragma warning disable CA1031 // ranh giới tác vụ nền: một tài liệu lỗi không được chặn các tài liệu còn lại, lỗi được báo qua StatusText
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"WorkspaceIndexingRunner: lập chỉ mục '{document.Title}' lỗi: {ex}");
            return false;
        }
#pragma warning restore CA1031
    }

    private void OnDocumentFinished(WorkspaceDocument document, bool succeeded, int generation)
    {
        if (generation != _generation) return;

        // Gỡ khỏi tập đã xếp hàng cả khi lỗi: câu hỏi sau sẽ thử lại tài liệu còn thiếu.
        _queued.Remove(document.DocumentId);
        _remaining--;
        if (succeeded) _done++; else _failed++;
        StatusText = FormatStatus(_remaining, _done, _failed);
    }

    internal static string FormatStatus(int remaining, int done, int failed)
    {
        if (remaining > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Đang lập chỉ mục {remaining} tài liệu, câu trả lời chưa gồm các tài liệu này.");
        }

        if (failed > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Không lập được chỉ mục cho {failed} tài liệu; các tài liệu này chưa có trong câu trả lời.");
        }

        return done > 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"Đã lập chỉ mục xong {done} tài liệu. Hỏi lại để câu trả lời gồm cả các tài liệu này.")
            : string.Empty;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
