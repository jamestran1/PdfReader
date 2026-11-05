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
    private readonly CompositionsCallbacks _callbacks;
    private readonly TimeProvider _clock;
    private CancellationTokenSource? _generation;

    // Tra tên Document trong Library cho Note của Document đã ra khỏi Workspace; có sau AttachSources (trước đó không tra được).
    private Func<string, string?>? _documentTitle;

    internal CompositionsViewModel(
        ICompositionStore store,
        INoteStore noteStore,
        ICompositionGenerator generator,
        Func<IReadOnlyList<CompositionSourceDocument>> documentsInWorkspace,
        CompositionsCallbacks callbacks,
        TimeProvider? clock = null)
    {
        _store = store;
        _noteStore = noteStore;
        _generator = generator;
        _documentsInWorkspace = documentsInWorkspace;
        _callbacks = callbacks;
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
            new CompositionsCallbacks
            {
                OpenComposition = (composition, afterId) => host.Session.OpenComposition(composition, afterId),
                NotifyError = host.NotifyError,
                OpenTabFor = id => host.Session.FindCompositionTab(id),
                CloseTab = tab => host.Session.Close(tab),
                CopyToClipboard = DesktopPrompts.CopyText,
                ConfirmDelete = services.ConfirmDeleteComposition ?? DesktopPrompts.ConfirmDeleteComposition,
                BudgetChars = () => CompositionBudget.CharsFor(services.Settings.GetModelSettings().ChatModel),
            });
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
    [NotifyCanExecuteChangedFor(nameof(RegenerateCommand))]
    [NotifyCanExecuteChangedFor(nameof(ShowCreateDialogCommand))]
    public partial bool IsGenerating { get; private set; }

    /// <summary>Nạp Bản soạn của Workspace <paramref name="ownerKey"/> (null thì rỗng). Gọi khi đổi Workspace hoạt động.</summary>
    public void LoadFor(string? ownerKey)
    {
        // Đổi Workspace huỷ lần sinh đang chạy: không Bản soạn nào được lưu vào Workspace cũ hay mới.
        if (ownerKey != OwnerKey) CancelGeneration();
        OwnerKey = ownerKey;
        Items.Clear();
        if (ownerKey is null) return;
        foreach (var composition in _store.GetForOwner(ownerKey)) Items.Add(composition);
    }

    /// <summary>Huỷ lần sinh đang chạy (đổi Workspace, thoát app).</summary>
    [RelayCommand]
    public void CancelGeneration() => _generation?.Cancel();

    [RelayCommand]
    private void Open(Composition? composition)
    {
        if (composition is not null) _callbacks.OpenComposition(composition, null);
    }

    private bool CanGenerate() => !IsGenerating;

    /// <summary>Hộp thoại "Tạo Bản soạn" đang mở (hoặc vừa đóng); dựng mới mỗi lần bấm ＋.</summary>
    [ObservableProperty]
    public partial CreateCompositionDialogViewModel? Dialog { get; private set; }

    /// <summary>Bind hai chiều với <c>DialogHost.IsOpen</c> để bấm ra ngoài thì đóng.</summary>
    [ObservableProperty]
    public partial bool IsDialogOpen { get; set; }

    /// <summary>Nút ＋ ở mục Bản soạn: mở hộp thoại chọn Note, kiểu và yêu cầu thay vì sinh ngay.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private void ShowCreateDialog()
    {
        if (OwnerKey is not { } owner || IsGenerating) return;
        Dialog = new CreateCompositionDialogViewModel(
            _noteStore.GetForOwner(owner), _documentsInWorkspace(),
            draft => _ = CreateFromDraftAsync(draft),
            () => IsDialogOpen = false,
            _callbacks.BudgetChars(), _documentTitle);
        IsDialogOpen = true;
    }

    /// <summary>Sinh Bản soạn từ lựa chọn của hộp thoại: đúng các Note đã chọn, kiểu và yêu cầu thêm.</summary>
    internal async Task CreateFromDraftAsync(CompositionDraft draft)
    {
        if (OwnerKey is not { } owner || IsGenerating) return;
        await GenerateAsync(new GenerationRequest(owner, draft.Kind, draft.NoteIds, AfterCompositionId: null, draft.AdditionalRequest));
    }

    /// <summary>Sinh lại: Bản soạn mới cùng kiểu, cùng Note nguồn và cùng yêu cầu thêm, mở bên phải tab cũ; bản cũ giữ nguyên.</summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task RegenerateAsync(Composition? source)
    {
        if (source is null || OwnerKey is not { } owner || IsGenerating) return;
        await GenerateAsync(new GenerationRequest(owner, source.Kind, source.SourceNoteIds, source.Id, source.AdditionalRequest));
    }

    // Gom tham số để hai string? liền nhau (AfterCompositionId, AdditionalRequest) không bị đảo chỗ mà compiler không bắt (CS-9).
    private sealed record GenerationRequest(
        string Owner, CompositionKind Kind, IEnumerable<string> NoteIds, string? AfterCompositionId, string? AdditionalRequest);

    private async Task GenerateAsync(GenerationRequest request)
    {
        var (owner, kind, noteIds, afterCompositionId, additionalRequest) = request;
        using var generation = new CancellationTokenSource();
        _generation = generation;
        IsGenerating = true;
        // Đọc Note và dựng context cũng là I/O: phải nằm trong try để lỗi được báo cho người dùng
        // (đường hộp thoại bỏ task nên lỗi ném ra ngoài sẽ mất im lặng).
        try
        {
            var wanted = new HashSet<string>(noteIds, StringComparer.Ordinal);
            var notes = _noteStore.GetForOwner(owner).Where(n => wanted.Contains(n.Id)).ToList();
            var context = CompositionContextBuilder.Build(notes, _documentsInWorkspace(), new CompositionContextOptions(additionalRequest, _documentTitle));
            if (context.SourceNoteIds.Count == 0)
            {
                _callbacks.NotifyError(NoNotesMessage);
                return;
            }

            // Sinh lại đi thẳng vào đây (không qua hộp thoại) nên ngân sách phải được chặn ở đây, trước khi gửi.
            int over = context.CharCount - _callbacks.BudgetChars();
            if (over > 0)
            {
                _callbacks.NotifyError(CreateCompositionDialogViewModel.OverBudgetMessage(over));
                return;
            }

            var generated = await _generator.GenerateAsync(kind, context, generation.Token);
            generation.Token.ThrowIfCancellationRequested();
            var composition = new Composition(
                Guid.NewGuid().ToString("N"), owner, generated.Title, kind,
                generated.Content, generated.ChatModel, context.SourceNoteIds, _clock.GetUtcNow().ToUnixTimeMilliseconds(),
                additionalRequest);
            _store.Add(composition);

            Items.Insert(0, composition);
            _callbacks.OpenComposition(composition, afterCompositionId);
        }
        catch (OperationCanceledException) when (generation.IsCancellationRequested)
        {
            // người dùng huỷ hoặc đổi Workspace: không lưu, không báo lỗi
        }
        catch (AiChatException ex)
        {
            _callbacks.NotifyError(AiErrorMessages.Describe(ex.Error));
        }
#pragma warning disable CA1031 // ranh giới lệnh UI: lỗi lưu không được làm sập app, hiện cho người dùng
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"CompositionsViewModel: tạo Bản soạn lỗi: {ex}");
            _callbacks.NotifyError(AiErrorMessages.Unknown);
        }
#pragma warning restore CA1031
        finally
        {
            IsGenerating = false;
            _generation = null;
        }
    }
}
