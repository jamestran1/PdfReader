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
    public static MainViewModelServices Create() => new(
        new ITextPdfDocumentService(),
        new WindowsSettingsService(),
        new OpenAiChatClientFactory(),
        CreateDocumentIndex(),
        new OpenAiEmbeddingGeneratorFactory(),
        CreateChatHistory(),
        CreateNoteStore(),
        CreateWorkspaceStore(),
        CreateThemeService(),
        CreateLibrary());

    public static SqliteDocumentIndex CreateDocumentIndex() => new(
        DataPath("index.db"),
        Path.Combine(AppContext.BaseDirectory, "vec0.dll"));

    public static IChatHistoryStore CreateChatHistory() => new SqliteChatHistoryStore(DataPath("chats.db"));

    public static INoteStore CreateNoteStore() => new SqliteNoteStore(DataPath("notes.db"));

    public static IWorkspaceStore CreateWorkspaceStore() => new SqliteWorkspaceStore(DataPath("workspaces.db"));

    public static IThemeService CreateThemeService() => new MaterialDesignThemeService();

    public static LibraryService CreateLibrary()
    {
        var libraryStore = new SqliteLibraryStore(DataPath("library.db"));
        libraryStore.EnsureSchema();
        return new LibraryService(libraryStore,
            DataPath("library"),
            Path.Combine(DataPath("library"), "thumbs"),
            new RenderEngine());
    }

    private static string DataPath(string name) => Path.Combine(AppDataDirectory.Current(), name);
}
