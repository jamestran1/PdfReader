using System.IO;
using PdfReaderApp.Core;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// Ghim cách dịch vụ mặc định được dựng: mọi cơ sở dữ liệu nằm dưới thư mục dữ liệu. Test qua constructor đặt
/// TRITHU_DATA_DIR cho cả tiến trình nên cả class chạy không song song với test khác.
/// </summary>
[Collection(DataDirEnvironmentSerial.Name)]
public sealed class MainViewModelDefaultServicesTests : IDisposable
{
    private static readonly string[] DatabaseFiles = ["index.db", "chats.db", "notes.db", "workspaces.db", "library.db"];

    private readonly string? _previousDataDir = Environment.GetEnvironmentVariable(AppDataDirectory.OverrideVariable);
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppDataDirectory.OverrideVariable, _previousDataDir);
        // Dọn best-effort: thư mục tạm, hệ điều hành sẽ dọn nếu SQLite còn giữ file.
        try { Directory.Delete(_dataDir, recursive: true); }
        catch (IOException) { /* bỏ qua */ }
        catch (UnauthorizedAccessException) { /* bỏ qua */ }
    }

    private static string TempSettingsDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Create_WithDataDir_PlacesAllDatabasesUnderThatDir()
    {
        Directory.CreateDirectory(_dataDir);

        var services = DefaultServices.Create(_dataDir);
        try
        {
            services.DocumentIndex.EnsureSchema();
            services.ChatHistory.EnsureSchema();
            services.NoteStore.EnsureSchema();
            services.WorkspaceStore.EnsureSchema();

            foreach (string name in DatabaseFiles)
                Assert.True(File.Exists(Path.Combine(_dataDir, name)), name);
        }
        finally
        {
            services.DocumentIndex.Dispose();
        }
    }

    [Fact]
    public void Constructor_WithNullOptionalServices_CreatesDefaultStoresUnderDataDir()
    {
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable(AppDataDirectory.OverrideVariable, _dataDir);

        using var vm = new MainViewModel(
            new ITextPdfDocumentService(),
            new WindowsSettingsService(TempSettingsDir()),
            new OpenAiChatClientFactory(),
            DefaultServices.CreateDocumentIndex(_dataDir),
            new OpenAiEmbeddingGeneratorFactory());

        foreach (string name in new[] { "chats.db", "notes.db", "workspaces.db", "library.db" })
            Assert.True(File.Exists(Path.Combine(_dataDir, name)), name);
        Assert.NotNull(vm.Notes);
        Assert.Empty(vm.Library);
    }
}
