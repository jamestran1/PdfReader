using System.IO;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// Ghim cách <see cref="DefaultServices"/> dựng dịch vụ mặc định: mọi cơ sở dữ liệu nằm dưới thư mục dữ liệu
/// được truyền vào. Không đụng biến môi trường nên chạy song song an toàn.
/// </summary>
public sealed class MainViewModelDefaultServicesTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose()
    {
        // Dọn best-effort: SQLite có thể còn giữ file khi pool chưa đóng.
        try { Directory.Delete(_dataDir, recursive: true); }
        catch (IOException) { /* thư mục tạm, hệ điều hành sẽ dọn */ }
        catch (UnauthorizedAccessException) { /* thư mục tạm, hệ điều hành sẽ dọn */ }
    }

    [Fact]
    public void Create_WithDataDir_PlacesAllDatabasesUnderThatDir()
    {
        Directory.CreateDirectory(_dataDir);

        var services = DefaultServices.Create(_dataDir);
        services.DocumentIndex.EnsureSchema();
        services.ChatHistory.EnsureSchema();
        services.NoteStore.EnsureSchema();
        services.WorkspaceStore.EnsureSchema();

        foreach (string name in new[] { "index.db", "chats.db", "notes.db", "workspaces.db", "library.db" })
            Assert.True(File.Exists(Path.Combine(_dataDir, name)), name);
    }
}
