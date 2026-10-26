using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Hàng đợi lập chỉ mục nền cho các Document của Workspace mà câu hỏi Toàn Workspace chưa dùng được.
/// Chạy tuần tự từng tài liệu, huỷ được (CS-13), và đưa tiến độ thành <see cref="StatusText"/> cho khung chat.</summary>
public sealed partial class WorkspaceIndexingRunner : ObservableObject, IDisposable
{
    private readonly Func<WorkspaceDocument, CancellationToken, Task<DocumentIndexStatus>> _indexDocumentAsync;
    private readonly Action<Action> _postToUiThread;
    // Các trường dưới đây chỉ đọc/ghi trên luồng UI (Enqueue, Cancel và các lần cập nhật đều qua _postToUiThread).
    private readonly HashSet<string> _queued = new(StringComparer.Ordinal);
    private CancellationTokenSource _cts = new();
    // Tăng mỗi lần huỷ hoặc Dispose: báo cáo của lượt đã bị huỷ có thể tới muộn và không được ghi đè trạng thái mới.
    private int _generation;
    private IndexingTally _tally = new(0, 0, 0);
    private readonly int _externallyIndexing;

    /// <summary>Số liệu để dựng thông báo: còn chờ, đã lập xong (đạt Complete), không lập được.</summary>
    internal readonly record struct IndexingTally(int Remaining, int Done, int Failed);

    /// <summary>Thông báo cho người dùng; rỗng khi không có tài liệu nào đang chờ hoặc vừa lập xong.</summary>
    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Tác vụ nền gần nhất, null nếu chưa chạy. Để test chờ xác định thay vì ngủ.</summary>
    internal Task? BackgroundTask { get; private set; }

    /// <param name="indexDocumentAsync">Lập chỉ mục một tài liệu và trả trạng thái index cuối cùng; ném
    /// <see cref="OperationCanceledException"/> khi bị huỷ.</param>
    /// <param name="postToUiThread">Đưa thao tác về luồng UI (không chặn luồng nền); truyền từ nơi dựng,
    /// test truyền bản chạy tại chỗ vì không có Application.</param>
    public WorkspaceIndexingRunner(
        Func<WorkspaceDocument, CancellationToken, Task<DocumentIndexStatus>> indexDocumentAsync,
        Action<Action> postToUiThread)
    {
        _indexDocumentAsync = indexDocumentAsync;
        _postToUiThread = postToUiThread;
    }

    /// <summary>Thêm tài liệu vào cuối hàng đợi; tài liệu đã nằm trong hàng đợi thì bỏ qua. Gọi trên luồng UI.</summary>
    public void Enqueue(IReadOnlyList<WorkspaceDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        var fresh = documents.Where(d => _queued.Add(d.DocumentId)).ToList();
        if (fresh.Count == 0) return;

        if (_tally.Remaining == 0) _tally = new IndexingTally(0, 0, 0);

        _tally = _tally with { Remaining = _tally.Remaining + fresh.Count };
        Publish();

        int generation = _generation;
        CancellationToken ct = _cts.Token;
        Task previous = BackgroundTask ?? Task.CompletedTask;
        // Không truyền ct cho Task.Run: huỷ trước khi bắt đầu sẽ biến tác vụ thành Canceled và làm lượt sau ném khi chờ nó.
        BackgroundTask = Task.Run(() => RunAsync(previous, fresh, generation, ct));
    }

    /// <summary>Báo số tài liệu đang được lập chỉ mục bởi tác vụ khác (vd tài liệu đang mở) để thông báo tính cả chúng.
    /// Gọi mỗi lần hỏi: đặt lại số đếm, 0 nghĩa là không còn.</summary>
    public void SetExternallyIndexing(int count)
    {
    }

    /// <summary>Huỷ việc đang chạy và xoá hàng đợi, thông báo. Gọi khi đổi Workspace
    /// (<c>ChatViewModel.LoadHistory</c> thấy owner khác) và từ <see cref="Dispose"/>.</summary>
    public void Cancel()
    {
        _cts.Cancel();
        _cts.Dispose();
        _cts = new CancellationTokenSource();
        _generation++;
        _queued.Clear();
        _tally = new IndexingTally(0, 0, 0);
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
                DocumentIndexStatus? outcome = await TryIndexAsync(document, ct).ConfigureAwait(false);
                _postToUiThread(() => OnDocumentFinished(document, outcome, generation));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Huỷ có chủ đích (Cancel/Dispose): kết thúc êm, không báo lỗi.
        }
    }

    private async Task<DocumentIndexStatus?> TryIndexAsync(WorkspaceDocument document, CancellationToken ct)
    {
        try
        {
            return await _indexDocumentAsync(document, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
#pragma warning disable CA1031 // ranh giới tác vụ nền: một tài liệu lỗi không được chặn các tài liệu còn lại, lỗi được báo qua StatusText
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"WorkspaceIndexingRunner: lập chỉ mục '{document.Title}' lỗi: {ex}");
            return null;
        }
#pragma warning restore CA1031
    }

    private void OnDocumentFinished(WorkspaceDocument document, DocumentIndexStatus? outcome, int generation)
    {
        if (generation != _generation) return;

        // Gỡ khỏi tập đã xếp hàng cả khi lỗi: câu hỏi sau sẽ thử lại tài liệu còn thiếu.
        _queued.Remove(document.DocumentId);
        _tally = _tally with { Remaining = _tally.Remaining - 1 };
        _tally = outcome is null
            ? _tally with { Failed = _tally.Failed + 1 }
            : _tally with { Done = _tally.Done + 1 };
        Publish();
    }

    private void Publish() => StatusText = FormatStatus(_tally);

    internal static string FormatStatus(IndexingTally tally)
    {
        if (tally.Remaining > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Đang lập chỉ mục {tally.Remaining} tài liệu, câu trả lời chưa gồm các tài liệu này.");
        }

        if (tally.Failed > 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"Không lập được chỉ mục cho {tally.Failed} tài liệu; các tài liệu này chưa có trong câu trả lời.");
        }

        return tally.Done > 0
            ? string.Create(CultureInfo.InvariantCulture,
                $"Đã lập chỉ mục xong {tally.Done} tài liệu. Hỏi lại để câu trả lời gồm cả các tài liệu này.")
            : string.Empty;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
    }
}
