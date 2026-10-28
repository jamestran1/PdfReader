using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Lập chỉ mục nền cho tài liệu đang mở: trạng thái hiển thị, lập chỉ mục lại và huỷ khi đổi
/// tài liệu hoặc Dispose (CS-13). Tách khỏi MainViewModel để test trọn vẹn.</summary>
public sealed partial class IndexingViewModel : ObservableObject, IDisposable, IReadingSessionIndexing
{
    /// <summary>Báo khi lượt này không giành được claim vì lượt khác (vd hàng đợi Workspace) đang lập chỉ mục đúng tài liệu.</summary>
    internal const string IndexingElsewhereMessage = "Tài liệu đang được lập chỉ mục ở nền, kết quả AI sẽ dùng được khi xong.";

    private readonly IDocumentIndex _documentIndex;
    private readonly DocumentIndexingService _indexingService;
    private readonly Func<IndexingTarget?> _currentTarget;
    private readonly Action<Action> _runOnUiThread;
    private CancellationTokenSource? _cts;
    private IndexingRun? _currentRun;

    /// <summary>Danh tính một lượt lập chỉ mục. Progress&lt;T&gt; post báo tiến độ bất đồng bộ nên báo cáo của
    /// lượt đã bị thay thế (đổi tài liệu) hoặc đã lỗi có thể tới muộn và ghi đè trạng thái mới hơn; mọi lần
    /// ghi trạng thái phải qua <see cref="IsLive"/>. Chỉ đọc/ghi trên luồng UI nên không cần khoá.</summary>
    private sealed class IndexingRun
    {
        public bool Faulted { get; set; }
    }

    private bool IsLive(IndexingRun run) => ReferenceEquals(run, _currentRun) && !run.Faulted;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

    /// <summary>Tác vụ lập chỉ mục nền gần nhất, null nếu chưa chạy lần nào. Để test chờ xác định thay vì ngủ.</summary>
    internal Task? BackgroundTask { get; private set; }

    /// <param name="documentIndex">Chỉ mục để xoá khi lập chỉ mục lại.</param>
    /// <param name="indexingService">Dịch vụ thực hiện việc lập chỉ mục.</param>
    /// <param name="currentTarget">Trả về tài liệu đang mở, hoặc null khi chưa mở tài liệu nào.</param>
    /// <param name="runOnUiThread">Chạy thao tác trên luồng UI; mặc định dùng Dispatcher của ứng dụng,
    /// test truyền bản chạy tại chỗ vì không có Application.</param>
    public IndexingViewModel(
        IDocumentIndex documentIndex,
        DocumentIndexingService indexingService,
        Func<IndexingTarget?> currentTarget,
        Action<Action>? runOnUiThread = null)
    {
        _documentIndex = documentIndex;
        _indexingService = indexingService;
        _currentTarget = currentTarget;
        _runOnUiThread = runOnUiThread ?? RunOnApplicationDispatcher;
    }

    private static void RunOnApplicationDispatcher(Action action)
        => System.Windows.Application.Current?.Dispatcher.Invoke(action);

    /// <summary>Lập chỉ mục tài liệu đang mở ở nền; huỷ lượt đang chạy trước đó (đổi tài liệu).</summary>
    public void StartBackgroundIndexing()
    {
        IndexingTarget? target = _currentTarget();
        if (target is null) return;

        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;
        var run = new IndexingRun();
        _currentRun = run;

        // Progress<> bắt SynchronizationContext của luồng UI đang gọi, nên cập nhật trạng thái về đúng luồng UI.
        var progress = new Progress<IndexingProgress>(p =>
        {
            if (IsLive(run)) StatusText = FormatProgress(p);
        });

        // Lượt mới chờ lượt cũ nhả claim rồi mới chạy: lượt cũ chỉ nhả khi huỷ chạm tới nó (thường sau khi lời gọi embedding
        // đang bay trả về). Chạy ngay thì TryBeginIndexing thấy Indexing và bỏ qua, rồi lượt cũ nhả về Partial và không ai lập lại.
        // IndexInBackgroundAsync tự bắt mọi lỗi nên chờ nó không bao giờ ném. Không truyền ct cho Task.Run: huỷ trước khi bắt
        // đầu sẽ biến tác vụ thành Canceled và làm lượt sau ném khi chờ.
        Task previous = BackgroundTask ?? Task.CompletedTask;
        BackgroundTask = Task.Run(async () =>
        {
            await previous.ConfigureAwait(false);
            await IndexInBackgroundAsync(target, progress, run, ct).ConfigureAwait(false);
        });
    }

    private async Task IndexInBackgroundAsync(
        IndexingTarget target, IProgress<IndexingProgress> progress, IndexingRun run, CancellationToken ct)
    {
        try
        {
            IndexingOutcome outcome = await _indexingService.IndexAsync(
                target.DocumentId, target.FilePath, target.PageTexts, progress, ct);
            if (outcome == IndexingOutcome.SkippedAnotherRunIndexing)
            {
                // Lượt khác giữ claim hợp lệ (vd hàng đợi Workspace): báo thay vì im lặng. Superseded thì lượt mới đã tự báo.
                _runOnUiThread(() =>
                {
                    if (IsLive(run)) StatusText = IndexingElsewhereMessage;
                });
            }
        }
        catch (OperationCanceledException) { }
#pragma warning disable CA1031 // ranh giới tác vụ nền: lập chỉ mục lỗi được phân loại và báo cho người dùng
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"IndexingViewModel: lập chỉ mục lỗi: {ex}");
            string message = FormatError(ex);
            _runOnUiThread(() =>
            {
                if (!ReferenceEquals(run, _currentRun)) return;
                run.Faulted = true;
                StatusText = message;
            });
        }
#pragma warning restore CA1031
    }

    internal static string FormatProgress(IndexingProgress p)
        => string.Equals(p.Status, "complete", StringComparison.Ordinal)
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"Đang lập chỉ mục: {p.Done}/{p.Total}");

    // Không lộ ex.Message thô của SDK: phân loại rồi dùng lời báo tiếng Việt chung với chat.
    internal static string FormatError(Exception ex)
    {
        AiChatError error = AiErrorClassifier.Classify(ex);
        string reason = error == AiChatError.UnsupportedProvider
            ? AiErrorMessages.UnsupportedEmbeddingProvider()
            : AiErrorMessages.Describe(error);
        return $"Lập chỉ mục AI bị bỏ qua: {reason} Tìm kiếm văn bản vẫn dùng được.";
    }

    [RelayCommand]
    private void ReindexDocument()
    {
        IndexingTarget? target = _currentTarget();
        if (target is null) return;
        // Xoá vector của mọi model: trích lại text sinh chunk id mới nên vector của model nào cũng không còn khớp.
#pragma warning disable CA1031 // best-effort: xoá chỉ mục cũ lỗi không chặn việc lập chỉ mục lại
        try { _documentIndex.DeleteDocument(target.DocumentId); } catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"IndexingViewModel.Reindex: xoá chỉ mục cũ lỗi: {ex}"); }
#pragma warning restore CA1031
        StartBackgroundIndexing();
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
    }
}
