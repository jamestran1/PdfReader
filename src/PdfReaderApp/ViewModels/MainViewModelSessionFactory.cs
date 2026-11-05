using System.Windows;

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
    public ReadingSessionViewModel Create(SessionHostCallbacks host)
        => new(new ReadingSessionDependencies(
            DocumentLoader, WorkspaceStore, Search, Chat,
            new NotesReadingSessionPort(Notes, host.RefreshNotesDocumentContext),
            Indexing, Surface, host.NotifyError,
            DispatcherProvider: () => Application.Current?.Dispatcher,
            MarkDocumentOpened: host.MarkDocumentOpened,
            FindDocument: host.FindDocument,
            FindComposition: host.FindComposition));
}
