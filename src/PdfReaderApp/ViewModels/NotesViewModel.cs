using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>Quản lý ghi chú của sách đang mở: danh sách (lọc + sắp trong bộ nhớ, cập nhật tại chỗ),
/// soạn/sửa/xóa, click để nhảy tới trang neo. Tách khỏi MainViewModel để test trọn vẹn.</summary>
public sealed partial class NotesViewModel : ObservableObject
{
    private const int MaxNoteLength = 20000;

    private readonly INoteStore _store;
    private readonly Func<int?> _currentPageIndex;
    private readonly Action<int> _jumpToPageIndex;
    private readonly Func<string?>? _currentDocumentId;
    private readonly Action<string, int?>? _openDocument;

    private readonly List<Note> _all = []; // nguồn đầy đủ; Items là phần đã lọc/sắp
    private string? _ownerKey;
    private string? _editingId;

    public ObservableCollection<Note> Items { get; } = [];

    [ObservableProperty] public partial string Draft { get; set; } = string.Empty;
    [ObservableProperty] public partial bool IsEditing { get; set; }
    [ObservableProperty] public partial string FilterText { get; set; } = string.Empty;
    [ObservableProperty] public partial bool CanAddNote { get; set; }
    [ObservableProperty] public partial string StatusMessage { get; set; } = string.Empty;
    [ObservableProperty] public partial int RightTabIndex { get; set; }
    [ObservableProperty] public partial string? PendingQuote { get; set; }
    [ObservableProperty] public partial bool ShowDocumentChips { get; set; }
    [ObservableProperty] public partial bool FilterCurrentDocumentOnly { get; set; }
    [ObservableProperty] public partial double NotesScroll { get; set; }
    [ObservableProperty] public partial string? SelectedNoteId { get; set; }

    // Từ điển tiêu đề tài liệu: documentId -> tiêu đề hiển thị (dùng cho chip nhãn)
    public IReadOnlyDictionary<string, string> DocumentTitles { get; private set; } = new Dictionary<string, string>(StringComparer.Ordinal);
    private int _pendingPageIndex;
    private const string DefaultHighlightColor = "#FFEB3B";
    private IReadOnlyList<HighlightRect>? _pendingRects;
    public ObservableCollection<Note> Highlights { get; } = [];

    public NotesViewModel(INoteStore store, Func<int?> currentPageIndex, Action<int> jumpToPageIndex,
        Func<string?>? currentDocumentId = null, Action<string, int?>? openDocument = null)
    {
        _store = store;
        _currentPageIndex = currentPageIndex;
        _jumpToPageIndex = jumpToPageIndex;
        _currentDocumentId = currentDocumentId;
        _openDocument = openDocument;
    }

    /// <summary>Cập nhật ngữ cảnh chip nhãn tài liệu. Gọi khi danh sách tài liệu workspace đổi.</summary>
    public void SetDocumentContext(IReadOnlyDictionary<string, string> titles, bool showChips)
    {
        DocumentTitles = titles;
        OnPropertyChanged(nameof(DocumentTitles));
        ShowDocumentChips = showChips;
    }

    // Sắp: trang tăng dần (null cuối), rồi tạo mới hơn lên trước.
    private static int CompareNotes(Note a, Note b)
    {
        int pa = a.PageIndex ?? int.MaxValue;
        int pb = b.PageIndex ?? int.MaxValue;
        if (pa != pb) return pa.CompareTo(pb);
        return b.CreatedAtUnixMs.CompareTo(a.CreatedAtUnixMs);
    }

    public bool MatchesFilter(Note n, string? filter)
    {
        if (FilterCurrentDocumentOnly && n.DocumentId != _currentDocumentId?.Invoke()) return false;
        return string.IsNullOrWhiteSpace(filter)
           || n.Content.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || (n.Quote != null && n.Quote.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    public void LoadFor(string? ownerKey)
    {
        CancelEdit();
        _ownerKey = ownerKey;
        CanAddNote = ownerKey != null;
        _all.Clear();
        if (ownerKey != null)
        {
            try { _all.AddRange(_store.GetForOwner(ownerKey)); }
#pragma warning disable CA1031 // degrade: lỗi store không làm hỏng UI
            catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"NotesViewModel.LoadFor: lỗi store: {ex}"); /* lỗi store không làm hỏng UI */ }
#pragma warning restore CA1031
        }
        RefreshForCurrentDocument();
    }

    partial void OnFilterTextChanged(string value) => RebuildItems();
    partial void OnFilterCurrentDocumentOnlyChanged(bool value) => RebuildItems();

    public void RefreshForCurrentDocument()
    {
        RebuildItems();
        Highlights.Clear();
        string? cur = _currentDocumentId?.Invoke();
        foreach (var n in _all)
            if (n.Rects != null && n.Rects.Count > 0 && n.DocumentId == cur) Highlights.Add(n);
    }

    private void RebuildItems()
    {
        Items.Clear();
        foreach (var n in _all.Where(n => MatchesFilter(n, FilterText)).OrderBy(n => n, Comparer<Note>.Create(CompareNotes)))
            Items.Add(n);
    }

    private void InsertSorted(Note note)
    {
        int i = 0;
        while (i < Items.Count && CompareNotes(note, Items[i]) >= 0) i++;
        Items.Insert(i, note);
    }

    // Bắt đầu tạo note từ vùng chọn trang: chuyển sang tab Notes, giữ trích dẫn + trang + rects chờ.
    public void BeginNoteFromSelection(string quote, int pageIndex, IReadOnlyList<HighlightRect> rects)
    {
        CancelEdit();
        PendingQuote = quote;
        _pendingPageIndex = pageIndex;
        _pendingRects = rects;
        RightTabIndex = 1; // 0=Chat, 1=Notes
    }

    // Tạo note trực tiếp (không qua Draft/composer). Dùng cho one-click lưu câu trả lời AI.
    // Trả true nếu đã lưu (đang mở sách + content không rỗng).
    public bool AddNote(string content, string? quote, int? pageIndex)
    {
        if (_ownerKey == null) return false;
        string text = (content ?? string.Empty).Trim();
        if (text.Length == 0) return false;
        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        string? docId = _currentDocumentId?.Invoke();
        var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, docId, pageIndex, quote, text, now, now);
        try { _store.Add(note); }
#pragma warning disable CA1031 // ranh giới UI: lưu note lỗi thì ghi log và trả false, không làm gãy UI; báo lỗi cho người dùng làm ở #165
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"NotesViewModel.AddNote: lưu note lỗi: {ex}"); return false; }
#pragma warning restore CA1031
        _all.Add(note);
        if (MatchesFilter(note, FilterText)) InsertSorted(note);
        return true;
    }

    [RelayCommand]
    private void Save()
    {
        StatusMessage = string.Empty;
        string content = (Draft ?? string.Empty).Trim();
        bool hasQuote = !string.IsNullOrEmpty(PendingQuote);
        if (content.Length == 0 && !hasQuote) return;   // cho phép quote-only
        if (_ownerKey == null) return;
        if (content.Length > MaxNoteLength)
        {
            StatusMessage = $"Ghi chú quá dài (tối đa {MaxNoteLength} ký tự).";
            return;
        }

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        bool saved = _editingId == null
            ? CreateNoteFromDraft(_ownerKey, content, hasQuote, now)
            : UpdateEditingNote(_ownerKey, _editingId, content, now);
        if (!saved) return;

        Draft = string.Empty;
        CancelEdit();
    }

    // Trả true nếu đã lưu xong và cần dọn composer; false nếu store lỗi.
    private bool CreateNoteFromDraft(string ownerKey, string content, bool hasQuote, long now)
    {
        int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();
        var rects = _pendingRects;
        var color = rects != null ? DefaultHighlightColor : null;
        string? docId = _currentDocumentId?.Invoke();
        var note = new Note(Guid.NewGuid().ToString("N"), ownerKey, docId,
            page, PendingQuote, content, now, now, rects, color);
        try { _store.Add(note); }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: lưu note lỗi thì ghi log, không làm gãy UI; báo lỗi cho người dùng làm ở #165
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"NotesViewModel.Save: tạo note lỗi: {ex}"); return false; }
#pragma warning restore CA1031
        _all.Add(note);
        if (MatchesFilter(note, FilterText)) InsertSorted(note);
        if (note.Rects != null && note.Rects.Count > 0) Highlights.Add(note);
        return true;
    }

    // Trả true nếu đã lưu xong và cần dọn composer; false nếu store lỗi hoặc note đã bị xóa nơi khác (khi đó nạp lại).
    private bool UpdateEditingNote(string ownerKey, string editingId, string content, long now)
    {
        int rows;
        try { rows = _store.Update(editingId, content, now); }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: cập nhật note lỗi thì ghi log, không làm gãy UI; báo lỗi cho người dùng làm ở #165
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"NotesViewModel.Save: cập nhật note lỗi: {ex}"); return false; }
#pragma warning restore CA1031
        if (rows == 0) { LoadFor(ownerKey); return false; } // note đã bị xóa nơi khác
        ApplyEditToLists(editingId, content, now);
        return true;
    }

    private void ApplyEditToLists(string editingId, string content, long now)
    {
        int ai = _all.FindIndex(n => n.Id == editingId);
        if (ai >= 0) _all[ai] = _all[ai] with { Content = content, UpdatedAtUnixMs = now };
        int ii = IndexInItems(editingId);
        if (ii < 0) return;
        if (MatchesFilter(_all[ai], FilterText)) Items[ii] = _all[ai];
        else Items.RemoveAt(ii);
    }

    [RelayCommand]
    private void BeginEdit(Note? note)
    {
        if (note == null) return;
        Draft = note.Content;
        _editingId = note.Id;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        Draft = string.Empty;
        _editingId = null;
        IsEditing = false;
        PendingQuote = null;
        _pendingRects = null;
    }

    [RelayCommand]
    private void Delete(Note? note)
    {
        if (note == null) return;
        if (_editingId == note.Id) CancelEdit();
        try { _store.Delete(note.Id); }
#pragma warning disable CA1031 // ranh giới [RelayCommand]: xoá note lỗi thì ghi log, không làm gãy UI; báo lỗi cho người dùng làm ở #165
        catch (Exception ex) { System.Diagnostics.Trace.TraceWarning($"NotesViewModel.Delete: xoá note lỗi: {ex}"); return; }
#pragma warning restore CA1031
        _all.RemoveAll(n => n.Id == note.Id);
        int ii = IndexInItems(note.Id);
        if (ii >= 0) Items.RemoveAt(ii);
        int hi = -1;
        for (int i = 0; i < Highlights.Count; i++) if (Highlights[i].Id == note.Id) { hi = i; break; }
        if (hi >= 0) Highlights.RemoveAt(hi);
    }

    [RelayCommand]
    private void Open(Note? note)
    {
        if (note is null) return;
        string? cur = _currentDocumentId?.Invoke();
        if (note.DocumentId != null && note.DocumentId != cur && _openDocument != null)
            _openDocument(note.DocumentId, note.PageIndex);
        else if (note.PageIndex is int p)
            _jumpToPageIndex(p);
    }

    private int IndexInItems(string id)
    {
        for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return i;
        return -1;
    }
}
