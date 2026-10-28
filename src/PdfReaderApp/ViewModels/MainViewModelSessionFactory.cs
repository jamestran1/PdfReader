namespace PdfReaderApp.ViewModels;

/// <summary>Ráp Phiên đọc từ các ViewModel con của MainViewModel; tách ra để MainViewModel không phải biết kiểu cầu nối và record phụ thuộc.</summary>
internal sealed record MainViewModelSessionFactory(
    IDocumentLoader DocumentLoader,
    Services.IWorkspaceStore WorkspaceStore,
    SearchViewModel Search,
    ChatViewModel Chat,
    NotesViewModel Notes,
    IndexingViewModel Indexing,
    WorkspaceDocumentsViewModel Surface)
{
    /// <param name="refreshNotesDocumentContext">Cập nhật ngữ cảnh chip tài liệu của Notes (cần Library và surface của MainViewModel).</param>
    /// <param name="notifyError">Báo lỗi cho người dùng.</param>
    /// <param name="findDocument">Tìm Document trong Library của MainViewModel theo id.</param>
    public ReadingSessionViewModel Create(
        Action refreshNotesDocumentContext, Action<string> notifyError, Func<string, Models.LibraryItem?> findDocument)
        => new(new ReadingSessionDependencies(
            DocumentLoader, WorkspaceStore, Search, Chat,
            new NotesReadingSessionPort(Notes, refreshNotesDocumentContext),
            Indexing, Surface, notifyError, FindDocument: findDocument));
}
