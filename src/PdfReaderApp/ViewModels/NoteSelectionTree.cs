using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Cây chọn nguồn của hộp thoại Tạo Bản soạn: gốc Workspace, Document, Note, mặc định tick hết. Lọc chỉ đổi phần hiển thị,
/// không đổi tick. Chỉ liệt kê Note có chữ (cùng điều kiện với <see cref="CompositionContextBuilder"/>).
/// </summary>
public sealed partial class NoteSelectionTree : ObservableObject
{
    private const string FreeNotesTitle = "Note không gắn Document";
    private const int SummaryMaxLength = 90;

    private readonly List<NoteSelectionNode> _allNotes;
    private int _bulkDepth;

    public NoteSelectionTree(IReadOnlyList<Note> notes, IReadOnlyList<CompositionSourceDocument> documentsInOrder)
    {
        ArgumentNullException.ThrowIfNull(notes);
        ArgumentNullException.ThrowIfNull(documentsInOrder);

        var usable = notes.Where(CompositionContextBuilder.HasText).ToList();
        var groups = new List<DocumentSelectionNode>();
        var placed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documentsInOrder)
        {
            if (!placed.Add(document.DocumentId)) continue;
            var inDocument = usable.Where(n => n.DocumentId == document.DocumentId)
                .OrderBy(n => n.PageIndex ?? -1).ThenBy(n => n.CreatedAtUnixMs);
            AddGroup(groups, document.Title, inDocument);
        }

        var free = usable.Where(n => n.DocumentId is null || !placed.Contains(n.DocumentId)).OrderBy(n => n.CreatedAtUnixMs);
        AddGroup(groups, FreeNotesTitle, free);

        Documents = groups;
        _allNotes = [.. groups.SelectMany(g => g.Notes)];
    }

    public IReadOnlyList<DocumentSelectionNode> Documents { get; }

    public int TotalCount => _allNotes.Count;

    public int SelectedCount => _allNotes.Count(n => n.IsChecked);

    /// <summary>Note đang được chọn, theo thứ tự cây.</summary>
    public IReadOnlyList<Note> SelectedNotes => [.. _allNotes.Where(n => n.IsChecked).Select(n => n.Note)];

    /// <summary>Ô gốc Workspace: true hết, false không, null một phần. Đặt true chọn hết, false bỏ hết (xem <see cref="SelectionState.Resolve"/> cho null).</summary>
    public bool? IsChecked
    {
        get => SelectionState.Of(SelectedCount, TotalCount);
        set => SetAll(SelectionState.Resolve(value, IsChecked));
    }

    [ObservableProperty]
    public partial string FilterText { get; set; } = string.Empty;

    /// <summary>Tick đổi (một lần cho mỗi thao tác, kể cả hàng loạt).</summary>
    public event EventHandler? SelectionChanged;

    partial void OnFilterTextChanged(string value) => ApplyFilter(value);

    private void AddGroup(List<DocumentSelectionNode> groups, string title, IEnumerable<Note> notes)
    {
        var nodes = notes.Select(n => new NoteSelectionNode(n, SummaryOf(n), PageTextOf(n))).ToList();
        if (nodes.Count == 0) return;
        var group = new DocumentSelectionNode(title, nodes) { Applied = (g, value) => SetGroup(g, value) };
        foreach (var node in nodes) node.Changed = () => OnNoteChanged(group);
        groups.Add(group);
    }

    private void SetAll(bool value)
    {
        RunBulk(() =>
        {
            foreach (var node in _allNotes) node.IsChecked = value;
        });
    }

    private void SetGroup(DocumentSelectionNode group, bool value)
    {
        RunBulk(() =>
        {
            foreach (var node in group.Notes) node.IsChecked = value;
        });
    }

    private void RunBulk(Action apply)
    {
        _bulkDepth++;
        try { apply(); }
        finally { _bulkDepth--; }
        RaiseChanged();
    }

    private void OnNoteChanged(DocumentSelectionNode group)
    {
        if (_bulkDepth > 0) return;
        RaiseChanged(group);
    }

    private void RaiseChanged(DocumentSelectionNode? only = null)
    {
        foreach (var group in only is null ? Documents : [only]) group.RaiseSelectionChanged();
        OnPropertyChanged(nameof(IsChecked));
        OnPropertyChanged(nameof(SelectedCount));
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ApplyFilter(string text)
    {
        string needle = Normalize(text);
        foreach (var group in Documents)
        {
            bool titleMatches = needle.Length == 0 || Normalize(group.Title).Contains(needle, StringComparison.Ordinal);
            foreach (var node in group.Notes)
                node.IsVisible = titleMatches || Normalize($"{node.Note.Content} {node.Note.Quote}").Contains(needle, StringComparison.Ordinal);
            group.IsVisible = group.Notes.Any(n => n.IsVisible);
        }
    }

    // Không phân biệt hoa thường và dấu, để gõ "doan" ra "Đoạn".
    private static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        var builder = new StringBuilder();
        foreach (char c in text.Trim().Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            builder.Append(c is 'đ' or 'Đ' ? 'd' : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    private static string SummaryOf(Note note)
    {
        string source = string.IsNullOrWhiteSpace(note.Content) ? note.Quote! : note.Content;
        string line = source.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)[0];
        return line.Length <= SummaryMaxLength ? line : line[..SummaryMaxLength] + "…";
    }

    private static string PageTextOf(Note note) => note.PageIndex is int page ? $"tr. {page + 1}" : "không trang";
}
