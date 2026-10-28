namespace PdfReaderApp.ViewModels;

/// <summary>UI panel notes của một Tab: vị trí cuộn, note đang chọn và bộ lọc "chỉ tài liệu này".</summary>
public sealed record NotesPanelState(double NotesScroll, string? SelectedNoteId, bool FilterCurrentDocumentOnly);
