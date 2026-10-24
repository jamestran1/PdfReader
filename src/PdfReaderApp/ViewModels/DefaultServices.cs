using System.IO;
using PdfReaderApp.Core;
using PdfReaderApp.Services;

namespace PdfReaderApp.ViewModels;

/// <summary>
/// Dựng bản mặc định của <see cref="MainViewModelServices"/>. Mỗi method dựng mới một đối tượng,
/// không giữ state tĩnh (CS-11); đường dẫn dữ liệu theo <c>TRITHU_DATA_DIR</c> qua <see cref="AppDataDirectory"/>.
/// </summary>
public static class DefaultServices
{
    public static MainViewModelServices Create() => Create(AppDataDirectory.Current());

    /// <summary>Dựng bản mặc định với dữ liệu nằm dưới <paramref name="dataDir"/> (test truyền thư mục tạm).</summary>
    public static MainViewModelServices Create(string dataDir) => new(
        new ITextPdfDocumentService(),
        new WindowsSettingsService(),
        new OpenAiChatClientFactory(),
        CreateDocumentIndex(dataDir),
        new OpenAiEmbeddingGeneratorFactory(),
        CreateChatHistory(dataDir),
        CreateNoteStore(dataDir),
        CreateWorkspaceStore(dataDir),
        CreateThemeService(),
        CreateLibrary(dataDir));

    public static SqliteDocumentIndex CreateDocumentIndex(string dataDir) => new(
        Path.Combine(dataDir, "index.db"),
        Path.Combine(AppContext.BaseDirectory, "vec0.dll"));

    public static IChatHistoryStore CreateChatHistory(string dataDir) => new SqliteChatHistoryStore(Path.Combine(dataDir, "chats.db"));

    public static INoteStore CreateNoteStore(string dataDir) => new SqliteNoteStore(Path.Combine(dataDir, "notes.db"));

    public static IWorkspaceStore CreateWorkspaceStore(string dataDir) => new SqliteWorkspaceStore(Path.Combine(dataDir, "workspaces.db"));

    public static IThemeService CreateThemeService() => new MaterialDesignThemeService();

    public static LibraryService CreateLibrary(string dataDir)
    {
        var libraryStore = new SqliteLibraryStore(Path.Combine(dataDir, "library.db"));
        libraryStore.EnsureSchema();
        string libraryDir = Path.Combine(dataDir, "library");
        return new LibraryService(libraryStore,
            libraryDir,
            Path.Combine(libraryDir, "thumbs"),
            new RenderEngine());
    }
}
