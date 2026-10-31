using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Mục Bản soạn trên rail trái: danh sách Bản soạn của Workspace đang hoạt động (mới nhất trên cùng) và lệnh tạo Tóm tắt từ
/// mọi Note. Mở Bản soạn thành tab qua <c>openComposition</c> (Phiên đọc giữ việc đó), không tự giữ tab.
/// </summary>
public sealed partial class CompositionsViewModel : ObservableObject
{
    /// <summary>Thông báo khi Workspace chưa có Note nào để làm nguồn.</summary>
    public const string NoNotesMessage = "Workspace này chưa có Note nào để làm nguồn. Hãy thêm Note trước khi tạo Bản soạn.";

    private readonly ICompositionStore _store;
    private readonly INoteStore _noteStore;
    private readonly ICompositionGenerator _generator;
    private readonly Func<IReadOnlyList<CompositionSourceDocument>> _documentsInWorkspace;
    private readonly Action<Composition> _openComposition;
    private readonly Action<string> _notifyError;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _generation;

    internal CompositionsViewModel(
        ICompositionStore store,
        INoteStore noteStore,
        ICompositionGenerator generator,
        Func<IReadOnlyList<CompositionSourceDocument>> documentsInWorkspace,
        Action<Composition> openComposition,
        Action<string> notifyError,
        TimeProvider? clock = null)
    {
        _store = store;
        _noteStore = noteStore;
        _generator = generator;
        _documentsInWorkspace = documentsInWorkspace;
        _openComposition = openComposition;
        _notifyError = notifyError;
        _clock = clock ?? TimeProvider.System;
    }

    /// <summary>Dựng bản mặc định từ <see cref="MainViewModelServices"/>: store compositions.db và generator theo Chat Model đang chọn.</summary>
    internal static CompositionsViewModel Create(
        MainViewModelServices services,
        MainViewModel host)
    {
        var store = services.CompositionStore ?? DefaultServices.CreateCompositionStore(AppDataDirectory.Current());
        store.EnsureSchema();
        return new CompositionsViewModel(
            store, services.NoteStore,
            services.CompositionGenerator ?? new AiCompositionGenerator(services.Settings, services.ChatClientFactory),
            () => DocumentsInWorkspace(services.WorkspaceStore, host.Library, host.Session.ActiveWorkspaceId),
            composition => host.Session.OpenComposition(composition),
            host.NotifyError);
    }

    // Document của Workspace theo thứ tự Library, cùng thứ tự với surface Workspace Documents.
    private static List<CompositionSourceDocument> DocumentsInWorkspace(
        IWorkspaceStore workspaceStore, IEnumerable<LibraryItem> library, string? workspaceId)
    {
        if (workspaceId is null) return [];
        var memberIds = new HashSet<string>(workspaceStore.GetDocumentIds(workspaceId), StringComparer.Ordinal);
        return [.. library.Where(i => memberIds.Contains(i.DocumentId)).Select(i => new CompositionSourceDocument(i.DocumentId, i.Title))];
    }

    /// <summary>Đọc Bản soạn của Workspace theo id từ store (khôi phục Open Set); null khi đã bị xoá.</summary>
    public Composition? FindForOwner(string ownerKey, string compositionId)
        => _store.GetForOwner(ownerKey).FirstOrDefault(c => c.Id == compositionId);

    /// <summary>Xoá mọi Bản soạn của Workspace (khi xoá Workspace).</summary>
    public void DeleteForOwner(string ownerKey)
    {
        _store.DeleteForOwner(ownerKey);
        if (OwnerKey == ownerKey) Items.Clear();
    }

    /// <summary>Bản soạn của Workspace đang hoạt động, mới nhất trước.</summary>
    public ObservableCollection<Composition> Items { get; } = [];

    /// <summary>Workspace đang hoạt động (owner key của Bản soạn); null khi chưa có.</summary>
    public string? OwnerKey { get; private set; }

    /// <summary>Đang sinh một Bản soạn.</summary>
    [ObservableProperty]
    public partial bool IsGenerating { get; private set; }

    /// <summary>Nạp Bản soạn của Workspace <paramref name="ownerKey"/> (null thì rỗng). Gọi khi đổi Workspace hoạt động.</summary>
    public void LoadFor(string? ownerKey)
    {
        OwnerKey = ownerKey;
        Items.Clear();
        if (ownerKey is null) return;
        foreach (var composition in _store.GetForOwner(ownerKey)) Items.Add(composition);
    }

    /// <summary>Huỷ lần sinh đang chạy (đổi Workspace, thoát app).</summary>
    public void CancelGeneration() => _generation?.Cancel();

    [RelayCommand]
    private void Open(Composition? composition)
    {
        if (composition is not null) _openComposition(composition);
    }

    [RelayCommand]
    private async Task CreateSummaryAsync()
    {
        if (OwnerKey is not { } owner || IsGenerating) return;

        var context = CompositionContextBuilder.Build(_noteStore.GetForOwner(owner), _documentsInWorkspace());
        if (context.SourceNoteIds.Count == 0)
        {
            _notifyError(NoNotesMessage);
            return;
        }

        using var generation = new CancellationTokenSource();
        _generation = generation;
        IsGenerating = true;
        try
        {
            var generated = await _generator.GenerateAsync(CompositionKind.Summary, context, generation.Token);
            var composition = new Composition(
                Guid.NewGuid().ToString("N"), owner, generated.Title, CompositionKind.Summary,
                generated.Content, generated.ChatModel, context.SourceNoteIds, _clock.GetUtcNow().ToUnixTimeMilliseconds());
            _store.Add(composition);

            // Người dùng có thể đã đổi Workspace trong lúc chờ AI: Bản soạn vẫn lưu cho đúng Workspace gốc, nhưng không
            // chen vào rail hay mở tab ở Workspace khác.
            if (OwnerKey != owner) return;
            Items.Insert(0, composition);
            _openComposition(composition);
        }
        catch (OperationCanceledException) when (generation.IsCancellationRequested)
        {
            // người dùng huỷ: không lưu, không báo lỗi
        }
        catch (AiChatException ex)
        {
            _notifyError(AiErrorMessages.Describe(ex.Error));
        }
#pragma warning disable CA1031 // ranh giới lệnh UI: lỗi lưu không được làm sập app, hiện cho người dùng
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"CompositionsViewModel: tạo Bản soạn lỗi: {ex}");
            _notifyError(AiErrorMessages.Unknown);
        }
#pragma warning restore CA1031
        finally
        {
            IsGenerating = false;
            _generation = null;
        }
    }
}
