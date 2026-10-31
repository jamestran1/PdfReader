using CommunityToolkit.Mvvm.Input;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Trích dẫn <c>[n]</c> và danh sách nguồn của Tab Bản soạn (#295). Mỗi lần Tab được kích hoạt, nguồn được đọc lại từ Note
/// hiện tại (Bản soạn không đổi); bấm <c>[n]</c> nhờ host mở Note: Cross-doc jump tới Document, hoặc chọn Note tự do ở panel Notes.
/// </summary>
public sealed partial class CompositionSourcesViewModel
{
    private readonly INoteStore _noteStore;
    private readonly Func<string, string?> _documentTitle;
    private readonly Action<CompositionSourceEntry> _openDocumentNote;
    private readonly Action<CompositionSourceEntry> _selectFreeNote;
    private readonly Dictionary<string, CompositionSourceEntry> _entriesByNoteId = new(StringComparer.Ordinal);

    internal CompositionSourcesViewModel(
        INoteStore noteStore,
        Func<string, string?> documentTitle,
        Action<CompositionSourceEntry> openDocumentNote,
        Action<CompositionSourceEntry> selectFreeNote)
    {
        _noteStore = noteStore;
        _documentTitle = documentTitle;
        _openDocumentNote = openDocumentNote;
        _selectFreeNote = selectFreeNote;
    }

    /// <summary>Nối với Phiên đọc và panel Notes: Tab Bản soạn kích hoạt thì đọc lại nguồn; bấm <c>[n]</c> của Note có Document là
    /// Cross-doc jump (tô đoạn Note trích nếu có), của Note tự do thì không nhảy. Cả hai chọn Note ở panel Notes sau cùng,
    /// vì đổi Tab Document nạp lại trạng thái panel của Tab đó.</summary>
    internal static CompositionSourcesViewModel Create(
        INoteStore noteStore, Func<string, string?> documentTitle, ReadingSessionViewModel session, NotesViewModel notes)
    {
        void ShowNote(string noteId)
        {
            notes.FilterText = string.Empty;
            notes.FilterCurrentDocumentOnly = false;
            notes.RightTabIndex = 1; // 0=Chat, 1=Notes
            notes.SelectedNoteId = noteId;
        }

        var viewModel = new CompositionSourcesViewModel(
            noteStore, documentTitle,
            entry =>
            {
                session.JumpFromCitation(new RagSource(entry.Number, entry.DocumentId!, entry.DocumentTitle ?? string.Empty, entry.PageIndex ?? 0, entry.Quote), null);
                ShowNote(entry.NoteId);
            },
            entry => ShowNote(entry.NoteId));
        session.Tabs.ActiveTabChanged += tab =>
        {
            if (tab is CompositionTab compositionTab) viewModel.Refresh(compositionTab);
        };
        return viewModel;
    }

    /// <summary>Đọc lại Note nguồn của <paramref name="tab"/> và dựng lại danh sách nguồn cùng các link <c>[n]</c>.</summary>
    public void Refresh(CompositionTab tab)
    {
        ArgumentNullException.ThrowIfNull(tab);
        var entries = CompositionSources.Resolve(tab.Composition, _noteStore.GetForOwner(tab.Composition.OwnerKey), _documentTitle);
        foreach (var entry in entries)
        {
            if (entry.IsClickable) _entriesByNoteId[entry.NoteId] = entry;
            else _entriesByNoteId.Remove(entry.NoteId);
        }

        tab.ApplySources(entries);
    }

    [RelayCommand]
    private void OpenCitation(CitationSpan? span)
    {
        // RagSource.DocumentId mang id Note (xem CompositionSources.ParseCitations).
        if (span?.Source is not { } source || !_entriesByNoteId.TryGetValue(source.DocumentId, out var entry)) return;
        if (entry.IsFree) _selectFreeNote(entry);
        else _openDocumentNote(entry);
    }
}
