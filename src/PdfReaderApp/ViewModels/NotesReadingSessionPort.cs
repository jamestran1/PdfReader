namespace PdfReaderApp.ViewModels;

/// <summary>Cầu nối Notes với Phiên đọc: lộ UI panel notes và nạp Notes theo Workspace; ngữ cảnh chip tài liệu
/// cần Library và surface nên do MainViewModel cấp qua <paramref name="refreshDocumentContext"/>.</summary>
internal sealed class NotesReadingSessionPort(NotesViewModel notes, Action refreshDocumentContext) : IReadingSessionNotes
{
    public NotesPanelState PanelState
    {
        get => new(notes.NotesScroll, notes.SelectedNoteId, notes.FilterCurrentDocumentOnly);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            notes.NotesScroll = value.NotesScroll;
            notes.SelectedNoteId = value.SelectedNoteId;
            notes.FilterCurrentDocumentOnly = value.FilterCurrentDocumentOnly;
        }
    }

    public void LoadFor(string? workspaceId) => notes.LoadFor(workspaceId);

    public void RefreshDocumentContext() => refreshDocumentContext();
}
