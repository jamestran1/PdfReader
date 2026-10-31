using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

// Thanh công cụ Tab Bản soạn (#294): Sửa tự lưu, Đổi tên, Sao chép, Xoá. Phần phụ thuộc UI (hộp xác nhận, clipboard, đóng tab)
// vào qua các property bên dưới để test thay được; MainViewModel nối bản thật.
public sealed partial class CompositionsViewModel
{
    /// <summary>Quãng không gõ trước khi Sửa tự lưu.</summary>
    internal static readonly TimeSpan AutoSaveDelay = TimeSpan.FromMilliseconds(800);

    private readonly Dictionary<CompositionTab, ITimer> _pendingSaves = [];
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;

    /// <summary>Chạy việc trên luồng UI; mặc định đẩy về <see cref="SynchronizationContext"/> lúc dựng, test chạy thẳng.</summary>
    internal Action<Action>? RunOnUi { get; set; }

    /// <summary>Tab Bản soạn đang mở của <c>compositionId</c>, null khi chưa mở.</summary>
    internal Func<string, CompositionTab?> OpenTabFor { get; set; } = _ => null;

    /// <summary>Đóng tab (Tab dùng gần nhất được kích hoạt, theo ADR 0003).</summary>
    internal Action<CompositionTab> CloseTab { get; set; } = _ => { };

    /// <summary>Đặt văn bản lên clipboard.</summary>
    internal Action<string> CopyToClipboard { get; set; } = _ => { };

    /// <summary>Hỏi xác nhận xoá Bản soạn có tiêu đề này; mặc định không xoá.</summary>
    internal Func<string, bool> ConfirmDelete { get; set; } = _ => false;

    /// <summary>Bật hoặc tắt chế độ Sửa. Tắt thì lưu ngay phần đã gõ.</summary>
    [RelayCommand]
    private void ToggleEdit(CompositionTab? tab)
    {
        if (tab is null) return;
        if (tab.IsEditing)
        {
            tab.PropertyChanged -= OnTabPropertyChanged;
            SaveEdit(tab);
            tab.IsEditing = false;
            return;
        }

        tab.EditText = tab.Composition.Content;
        tab.IsEditing = true;
        tab.PropertyChanged += OnTabPropertyChanged;
    }

    private void OnTabPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CompositionTab.EditText) || sender is not CompositionTab tab) return;
        if (tab.EditText == tab.Composition.Content) return;
        ScheduleSave(tab);
    }

    // Mỗi lần gõ làm lại quãng chờ; hết quãng thì lưu. Timer chạy ở thread pool nên đưa việc lưu về luồng UI.
    private void ScheduleSave(CompositionTab tab)
    {
        if (_pendingSaves.Remove(tab, out var previous)) previous.Dispose();
        _pendingSaves[tab] = _clock.CreateTimer(
            _ => PostToUi(() => SaveEdit(tab)), null, AutoSaveDelay, Timeout.InfiniteTimeSpan);
    }

    private void PostToUi(Action action)
    {
        if (RunOnUi is not null) RunOnUi(action);
        else if (_uiContext is null) action();
        else _uiContext.Post(_ => action(), null);
    }

    /// <summary>Lưu mọi chữ đã gõ nhưng chưa tới quãng tự lưu (chuyển tab, đóng tab, đổi Workspace, thoát app).</summary>
    public void FlushPendingEdits()
    {
        foreach (var tab in _pendingSaves.Keys.ToArray()) SaveEdit(tab);
    }

    private void SaveEdit(CompositionTab tab)
    {
        if (_pendingSaves.Remove(tab, out var timer)) timer.Dispose();
        if (!tab.IsEditing || tab.EditText == tab.Composition.Content) return;
        Persist(tab.Composition with { Content = tab.EditText });
    }

    [RelayCommand]
    private static void StartRename(CompositionTab? tab)
    {
        if (tab is null) return;
        tab.RenameText = tab.Title;
        tab.IsRenaming = true;
    }

    [RelayCommand]
    private void CommitRename(CompositionTab? tab)
    {
        if (tab is null || !tab.IsRenaming) return;
        tab.IsRenaming = false;
        string title = tab.RenameText.Trim();
        if (title.Length == 0 || title == tab.Title) return;

        SaveEdit(tab);   // chữ đang gõ dở phải vào bản đã lưu trước khi đổi tên
        Persist(tab.Composition with { Title = title });
    }

    [RelayCommand]
    private static void CancelRename(CompositionTab? tab)
    {
        if (tab is not null) tab.IsRenaming = false;
    }

    [RelayCommand]
    private void Copy(CompositionTab? tab)
    {
        if (tab is null) return;
        CopyToClipboard(tab.IsEditing ? tab.EditText : tab.Composition.Content);
    }

    /// <summary>Xoá một Bản soạn sau khi xác nhận: khỏi db và rail, đóng tab nếu đang mở. Dùng từ thanh công cụ và từ rail.</summary>
    [RelayCommand]
    private void Delete(Composition? composition)
    {
        if (composition is null || !ConfirmDelete(composition.Title)) return;

        var tab = OpenTabFor(composition.Id);
        if (tab is not null) DiscardPendingEdit(tab);
        _store.Delete(composition.Id);
        RemoveFromItems(composition.Id);
        if (tab is not null) CloseTab(tab);
    }

    private void DiscardPendingEdit(CompositionTab tab)
    {
        tab.PropertyChanged -= OnTabPropertyChanged;
        if (_pendingSaves.Remove(tab, out var timer)) timer.Dispose();
    }

    private void RemoveFromItems(string id)
    {
        int index = Items.ToList().FindIndex(c => c.Id == id);
        if (index >= 0) Items.RemoveAt(index);
    }

    // Ghi bản mới (Sửa hoặc Đổi tên), rồi đồng bộ rail và tab đang mở.
    private void Persist(Composition changed)
    {
        long now = _clock.GetUtcNow().ToUnixTimeMilliseconds();
        var saved = changed with { UpdatedAtUnixMs = now };
        try
        {
            _store.Update(saved.Id, saved.Title, saved.Content, now);
        }
#pragma warning disable CA1031 // ranh giới lệnh UI: lỗi ghi không được làm sập app, hiện cho người dùng
        catch (Exception ex)
        {
            System.Diagnostics.Trace.TraceWarning($"CompositionsViewModel: lưu Bản soạn lỗi: {ex}");
            _notifyError(AiErrorMessages.Unknown);
            return;
        }
#pragma warning restore CA1031

        int index = Items.ToList().FindIndex(c => c.Id == saved.Id);
        if (index >= 0) Items[index] = saved;
        OpenTabFor(saved.Id)?.Apply(saved);
    }
}
