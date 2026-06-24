using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    private readonly List<Note> _all = new(); // nguồn đầy đủ; Items là phần đã lọc/sắp
    private string? _ownerKey;
    private string? _editingId;

    public ObservableCollection<Note> Items { get; } = new();

    [ObservableProperty] private string _draft = string.Empty;
    [ObservableProperty] private bool _isEditing;
    [ObservableProperty] private string _filterText = string.Empty;
    [ObservableProperty] private bool _canAddNote;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private int _rightTabIndex;
    [ObservableProperty] private string? _pendingQuote;
    private int _pendingPageIndex;
    private const string DefaultHighlightColor = "#FFEB3B";
    private IReadOnlyList<HighlightRect>? _pendingRects;
    public ObservableCollection<Note> Highlights { get; } = new();

    public NotesViewModel(INoteStore store, Func<int?> currentPageIndex, Action<int> jumpToPageIndex)
    {
        _store = store;
        _currentPageIndex = currentPageIndex;
        _jumpToPageIndex = jumpToPageIndex;
    }

    // Sắp: trang tăng dần (null cuối), rồi tạo mới hơn lên trước.
    private static int CompareNotes(Note a, Note b)
    {
        int pa = a.PageIndex ?? int.MaxValue;
        int pb = b.PageIndex ?? int.MaxValue;
        if (pa != pb) return pa.CompareTo(pb);
        return b.CreatedAtUnixMs.CompareTo(a.CreatedAtUnixMs);
    }

    public static bool MatchesFilter(Note n, string? filter)
        => string.IsNullOrWhiteSpace(filter)
           || n.Content.Contains(filter, StringComparison.OrdinalIgnoreCase)
           || (n.Quote != null && n.Quote.Contains(filter, StringComparison.OrdinalIgnoreCase));

    public void LoadFor(string? ownerKey)
    {
        CancelEdit();
        _ownerKey = ownerKey;
        CanAddNote = ownerKey != null;
        _all.Clear();
        if (ownerKey != null)
        {
            try { _all.AddRange(_store.GetForOwner(ownerKey)); }
            catch { /* lỗi store không làm hỏng UI */ }
        }
        RebuildItems();
        Highlights.Clear();
        foreach (var n in _all)
            if (n.Rects != null && n.Rects.Count > 0) Highlights.Add(n);
    }

    partial void OnFilterTextChanged(string value) => RebuildItems();

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
        var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey, pageIndex, quote, text, now, now);
        try { _store.Add(note); }
        catch { return false; }
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

        if (_editingId == null)
        {
            int? page = hasQuote ? _pendingPageIndex : _currentPageIndex();
            var rects = _pendingRects;
            var color = rects != null ? DefaultHighlightColor : null;
            var note = new Note(Guid.NewGuid().ToString("N"), _ownerKey, _ownerKey,
                page, PendingQuote, content, now, now, rects, color);
            try { _store.Add(note); }
            catch { return; }
            _all.Add(note);
            if (MatchesFilter(note, FilterText)) InsertSorted(note);
            if (note.Rects != null && note.Rects.Count > 0) Highlights.Add(note);
        }
        else
        {
            int rows;
            try { rows = _store.Update(_editingId, content, now); }
            catch { return; }
            if (rows == 0) { LoadFor(_ownerKey); return; } // note đã bị xóa nơi khác
            int ai = _all.FindIndex(n => n.Id == _editingId);
            if (ai >= 0) _all[ai] = _all[ai] with { Content = content, UpdatedAtUnixMs = now };
            int ii = IndexInItems(_editingId);
            if (ii >= 0)
            {
                if (MatchesFilter(_all[ai], FilterText)) Items[ii] = _all[ai];
                else Items.RemoveAt(ii);
            }
        }

        Draft = string.Empty;
        CancelEdit();
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
        catch { return; }
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
        if (note?.PageIndex is int p) _jumpToPageIndex(p);
    }

    private int IndexInItems(string id)
    {
        for (int i = 0; i < Items.Count; i++) if (Items[i].Id == id) return i;
        return -1;
    }
}
