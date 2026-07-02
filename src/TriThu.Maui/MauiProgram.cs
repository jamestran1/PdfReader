using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using PdfReaderApp.Platform;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;
using SkiaSharp.Views.Maui.Controls.Hosting;
using TriThu.Maui.Pages;
using TriThu.Maui.Platform;
using IPdfRenderService = PdfReaderApp.Services.IPdfRenderService;

namespace TriThu.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp()
            .UseMauiCommunityToolkit()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // Platform services
        builder.Services.AddSingleton<ISettingsService, MauiSettingsService>();
        builder.Services.AddSingleton<IThemeService, MauiThemeService>();
        builder.Services.AddSingleton<IUiDispatcher, MauiDispatcher>();
        builder.Services.AddSingleton<IFilePickerService, MauiFilePickerService>();
        builder.Services.AddSingleton<ISettingsDialogService, MauiSettingsDialogService>();
        builder.Services.AddSingleton<IPdfRenderService, DocnetPdfRenderService>();
        builder.Services.AddSingleton<IPdfDocumentService, ITextPdfDocumentService>();
        builder.Services.AddSingleton<IChatClientFactory, OpenAiChatClientFactory>();
        builder.Services.AddSingleton<IEmbeddingGeneratorFactory, OpenAiEmbeddingGeneratorFactory>();

        // Document index (SQLite + vec0)
        builder.Services.AddSingleton<IDocumentIndex>(sp =>
        {
            string appDir = FileSystem.AppDataDirectory;
            string dbPath = Path.Combine(appDir, "index.db");
            string vec0Path = Path.Combine(appDir, "vec0");
            var index = new SqliteDocumentIndex(dbPath, vec0Path);
            index.EnsureSchema();
            return index;
        });

        // Chat history store
        builder.Services.AddSingleton<IChatHistoryStore>(sp =>
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "chats.db");
            var store = new SqliteChatHistoryStore(dbPath);
            store.EnsureSchema();
            return store;
        });

        // Note store
        builder.Services.AddSingleton<INoteStore>(sp =>
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "notes.db");
            var store = new SqliteNoteStore(dbPath);
            store.EnsureSchema();
            return store;
        });

        // Workspace store
        builder.Services.AddSingleton<IWorkspaceStore>(sp =>
        {
            string dbPath = Path.Combine(FileSystem.AppDataDirectory, "workspaces.db");
            var store = new SqliteWorkspaceStore(dbPath);
            store.EnsureSchema();
            return store;
        });

        // ViewModels
        builder.Services.AddTransient<MainViewModel>();

        // Pages
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
