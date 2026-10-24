using System.IO;
using PdfReaderApp.Core;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// Ghim cách MainViewModel dựng dịch vụ mặc định: khi tham số tuỳ chọn là null, các store và thư viện
/// nằm dưới thư mục dữ liệu theo TRITHU_DATA_DIR. Cùng một collection để các test đổi biến môi trường của tiến trình không chạy song song với nhau.
/// </summary>
[Collection("TRITHU_DATA_DIR")]
public sealed class MainViewModelDefaultServicesTests : IDisposable
{
    private readonly string? _previousDataDir = Environment.GetEnvironmentVariable(AppDataDirectory.OverrideVariable);
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public MainViewModelDefaultServicesTests()
    {
        Environment.SetEnvironmentVariable(AppDataDirectory.OverrideVariable, _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(AppDataDirectory.OverrideVariable, _previousDataDir);
    }

    private static string TempSettingsDir()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Constructor_WithNullOptionalServices_CreatesDefaultStoresUnderDataDir()
    {
        using var vm = new MainViewModel(
            new ITextPdfDocumentService(),
            new WindowsSettingsService(TempSettingsDir()),
            new OpenAiChatClientFactory(),
            new SqliteDocumentIndex(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".db"),
                Path.Combine(AppContext.BaseDirectory, "vec0.dll")),
            new OpenAiEmbeddingGeneratorFactory());

        Assert.True(File.Exists(Path.Combine(_dataDir, "chats.db")));
        Assert.True(File.Exists(Path.Combine(_dataDir, "notes.db")));
        Assert.True(File.Exists(Path.Combine(_dataDir, "workspaces.db")));
        Assert.True(File.Exists(Path.Combine(_dataDir, "library.db")));
        Assert.NotNull(vm.Notes);
        Assert.Empty(vm.Library);
    }
}
