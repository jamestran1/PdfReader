using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Lập chỉ mục nền cho tài liệu đang mở: trạng thái hiển thị, lập chỉ mục lại và huỷ khi đổi
/// tài liệu hoặc Dispose (CS-13). Tách khỏi MainViewModel để test trọn vẹn.</summary>
public sealed partial class IndexingViewModel : ObservableObject, IDisposable
{
    private readonly IDocumentIndex _documentIndex;
    private readonly DocumentIndexingService _indexingService;
    private readonly Func<IndexingTarget?> _currentTarget;
    private readonly Action<Action> _runOnUiThread;
    private CancellationTokenSource? _cts;

    [ObservableProperty]
    public partial string StatusText { get; set; } = string.Empty;

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
        _cts = new CancellationTokenSource();
        CancellationToken ct = _cts.Token;

        // Progress<> bắt SynchronizationContext của luồng UI đang gọi, nên cập nhật trạng thái về đúng luồng UI.
        var progress = new Progress<IndexingProgress>(p => StatusText = FormatProgress(p));

        _ = Task.Run(() => IndexInBackgroundAsync(target, progress, ct), ct);
    }

    private async Task IndexInBackgroundAsync(
        IndexingTarget target, IProgress<IndexingProgress> progress, CancellationToken ct)
    {
        try { await _indexingService.IndexAsync(target.DocumentId, target.FilePath, target.PageTexts, progress, ct); }
        catch (OperationCanceledException) { }
#pragma warning disable CA1031 // ranh giới tác vụ nền: lập chỉ mục lỗi được phân loại và báo cho người dùng
        catch (Exception ex)
        {
            string message = FormatError(ex);
            _runOnUiThread(() => StatusText = message);
        }
#pragma warning restore CA1031
    }

    internal static string FormatProgress(IndexingProgress p)
        => string.Equals(p.Status, "complete", StringComparison.Ordinal)
            ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"Đang lập chỉ mục: {p.Done}/{p.Total}");

    internal static string FormatError(Exception ex) => AiErrorClassifier.Classify(ex) switch
    {
        AiChatError.InsufficientQuota => "Lập chỉ mục AI bị bỏ qua: tài khoản OpenAI hết credits/quota (tìm kiếm văn bản vẫn dùng được).",
        AiChatError.Unauthorized => "Lập chỉ mục AI bị bỏ qua: API key không hợp lệ (tìm kiếm văn bản vẫn dùng được).",
        AiChatError.Network => "Lập chỉ mục AI bị bỏ qua: không kết nối được (tìm kiếm văn bản vẫn dùng được).",
        _ => $"Lập chỉ mục lỗi: {ex.Message}"
    };

    [RelayCommand]
    private void ReindexDocument()
    {
        IndexingTarget? target = _currentTarget();
        if (target is null) return;
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
