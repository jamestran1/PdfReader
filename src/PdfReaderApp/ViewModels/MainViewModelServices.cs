using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Các dependency của <see cref="MainViewModel"/>. Gom lại để constructor chính chỉ nhận một đối tượng
/// và để phần dựng bản mặc định (đường dẫn AppData, Sqlite, OpenAI) nằm ngoài ViewModel.
/// </summary>
public sealed record MainViewModelServices(
    IPdfDocumentService DocumentService,
    ISettingsService Settings,
    IChatClientFactory ChatClientFactory,
    IDocumentIndex DocumentIndex,
    IEmbeddingGeneratorFactory EmbeddingFactory,
    IChatHistoryStore ChatHistory,
    INoteStore NoteStore,
    IWorkspaceStore WorkspaceStore,
    IThemeService ThemeService,
    LibraryService Library,
    IDocumentLoader? DocumentLoader = null,
    ICompositionStore? CompositionStore = null,
    ICompositionGenerator? CompositionGenerator = null);
