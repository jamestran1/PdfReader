using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;
using System.Windows;
using Xunit;

namespace PdfReaderApp.Tests;

public class MainViewModelTests
{
    private sealed class FakeChatHistoryStore : PdfReaderApp.Services.IChatHistoryStore
    {
        public readonly List<string> Deleted = new();
        public readonly List<(string doc, string role, string content)> Appended = new();
        private readonly List<PdfReaderApp.Models.ChatHistoryEntry> _entries = new();

        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix)
        {
            Appended.Add((documentId, role, content));
            _entries.Add(new PdfReaderApp.Models.ChatHistoryEntry(documentId, role, content, createdAtUnix));
        }
        public System.Collections.Generic.IReadOnlyList<PdfReaderApp.Models.ChatHistoryEntry> GetAll(string documentId)
            => _entries.Where(e => e.DocumentId == documentId).ToList();
        public void DeleteForDocument(string documentId) => Deleted.Add(documentId);
    }

    private sealed class FakeWorkspaceStore : PdfReaderApp.Services.IWorkspaceStore
    {
        public readonly List<PdfReaderApp.Models.Workspace> All = new();
        public void EnsureSchema() { }
        public void Upsert(PdfReaderApp.Models.Workspace w) { All.RemoveAll(x => x.Id == w.Id); All.Add(w); }
        public PdfReaderApp.Models.Workspace? Get(string id) => All.FirstOrDefault(w => w.Id == id);
        public IReadOnlyList<PdfReaderApp.Models.Workspace> GetAll(bool includeDefault)
            => All.Where(w => includeDefault || !w.IsDefault).ToList();
        public void AddDocument(string workspaceId, string documentId) { }
        public void RemoveDocument(string workspaceId, string documentId) { }
        public IReadOnlyList<string> GetDocumentIds(string workspaceId) => new List<string>();
        public IReadOnlyList<string> GetWorkspaceIdsForDocument(string documentId) => new List<string>();
        public PdfReaderApp.Models.Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs)
        {
            var existing = All.FirstOrDefault(w => w.IsDefault && w.DefaultDocumentId == documentId);
            if (existing != null) return existing;
            var ws = new PdfReaderApp.Models.Workspace(System.Guid.NewGuid().ToString("N"), name, true, documentId, nowUnixMs, nowUnixMs);
            All.Add(ws);
            return ws;
        }
        public void Rename(string id, string name, long nowUnixMs) { }
        public void Delete(string id) { All.RemoveAll(w => w.Id == id); }
    }

    private static string TempDb() =>
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db");

    private static MainViewModel VmWithChatStore(FakeChatHistoryStore store)
        => new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.OpenAiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.OpenAiEmbeddingGeneratorFactory(),
            store);

    private static MainViewModel VmWithWorkspaceStore(FakeWorkspaceStore wsStore)
        => new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.OpenAiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.OpenAiEmbeddingGeneratorFactory(),
            workspaceStore: wsStore);

    [Fact]
    public void RemoveLibraryItem_DeletesChatHistoryForThatDocument()
    {
        var store = new FakeChatHistoryStore();
        var vm = VmWithChatStore(store);
        var item = new PdfReaderApp.Models.LibraryItem("docX", "x.pdf", "/lib/x.pdf", null, 3, 1, 1);
        vm.Library.Add(item);

        vm.RemoveLibraryItemCommand.Execute(item);

        Assert.Contains("docX", store.Deleted);
    }

    [Fact]
    public void MemoryTurns_ExcludesErrorEmptyAndInterruptedAiTurns_KeepsRest()
    {
        var turns = new (string role, string content)[]
        {
            ("User", "Câu hỏi 1"),
            ("AI", "Trả lời tốt"),
            ("User", "Câu hỏi 2"),
            ("AI", "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key."),
            ("AI", "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng."),
            ("AI", "Một phần trả lời" + PdfReaderApp.Services.AiChatService.InterruptedSentinel),
            ("AI", ""),
            ("User", "Câu hỏi 3"),
        };

        var kept = MainViewModel.MemoryTurns(turns).ToList();

        // Giữ: mọi lượt User + lượt AI là câu trả lời thật.
        Assert.Contains(kept, t => t.content == "Câu hỏi 1");
        Assert.Contains(kept, t => t.content == "Câu hỏi 3");
        Assert.Contains(kept, t => t.content == "Trả lời tốt");
        // Bỏ: AI báo lỗi (kể cả lỗi suy ra từ MapError), AI rỗng, AI gián đoạn.
        Assert.DoesNotContain(kept, t => t.content.Contains("Chưa cấu hình"));
        Assert.DoesNotContain(kept, t => t.content.Contains("Không kết nối được"));
        Assert.DoesNotContain(kept, t => t.content.Contains(PdfReaderApp.Services.AiChatService.InterruptedSentinel));
        Assert.DoesNotContain(kept, t => t.role == "AI" && t.content.Length == 0);
    }


    [Fact]
    public void MainViewModel_ShouldInitializeWithDefaultValues()
    {
        var viewModel = new MainViewModel();
        Assert.Equal("Ultimate PDF Reader & Editor", viewModel.WindowTitle);
        Assert.Equal(1, viewModel.CurrentPage);
        Assert.Equal(1, viewModel.TotalPages);
        Assert.Equal(1.0, viewModel.ZoomLevel);
    }

    [Fact]
    public void NextPageCommand_ShouldIncrementPage_WhenNotAtLastPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 1 };
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(2, viewModel.CurrentPage);
    }

    [Fact]
    public void NextPageCommand_ShouldNotIncrementPage_WhenAtLastPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 5 };
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(5, viewModel.CurrentPage);
    }

    [Fact]
    public void PreviousPageCommand_ShouldDecrementPage_WhenNotAtFirstPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 2 };
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.CurrentPage);
    }

    [Fact]
    public void PreviousPageCommand_ShouldNotDecrementPage_WhenAtFirstPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 1 };
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.CurrentPage);
    }

    [Fact]
    public void ZoomInCommand_ShouldIncreaseZoomLevel()
    {
        var viewModel = new MainViewModel { ZoomLevel = 1.0 };
        viewModel.ZoomInCommand.Execute(null);
        Assert.Equal(1.2, viewModel.ZoomLevel, 1);
    }

    [Fact]
    public void ZoomOutCommand_ShouldDecreaseZoomLevel_WhenAboveMinimum()
    {
        var viewModel = new MainViewModel { ZoomLevel = 1.0 };
        viewModel.ZoomOutCommand.Execute(null);
        Assert.Equal(0.8, viewModel.ZoomLevel, 1);
    }

    [Fact]
    public void ZoomOutCommand_ShouldNotDecreaseZoomLevel_WhenAtMinimum()
    {
        var viewModel = new MainViewModel { ZoomLevel = 0.4 };
        viewModel.ZoomOutCommand.Execute(null);
        Assert.Equal(0.4, viewModel.ZoomLevel, 1);
    }

    [Fact]
    public void OnSearchQueryChanged_ClearsPageHighlight()
    {
        var vm = new MainViewModel();
        vm.SelectedSearchQuery = "abc";
        vm.SearchQuery = "x";
        Assert.Equal(string.Empty, vm.SelectedSearchQuery);
    }

    [Fact]
    public void SearchQuery_SetEmpty_ClearsResultsAndExecutedQuery()
    {
        var vm = new MainViewModel();
        vm.SearchQuery = "abc";
        vm.ExecutedSearchQuery = "abc";
        vm.SearchQuery = "";
        Assert.Empty(vm.SearchResults);
        Assert.Equal(string.Empty, vm.ExecutedSearchQuery);
    }

    [Fact]
    public void ClearSearchCommand_ClearsQueryAndResults()
    {
        var vm = new MainViewModel();
        vm.SearchQuery = "abc";
        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public void SelectSearchResult_SetsPageAndHighlightQuery()
    {
        var vm = new MainViewModel { SearchQuery = "hành" };
        var result = new SearchResult(2, "snip", 1);
        vm.SelectSearchResultCommand.Execute(result);
        Assert.Equal(3, vm.CurrentPage);
        Assert.Equal("hành", vm.SelectedSearchQuery);
    }

    [Fact]
    public void ViewMode_DefaultsToContinuous()
    {
        Assert.Equal(PdfViewMode.Continuous, new MainViewModel().ViewMode);
    }

    [Fact]
    public void ShowCoverSeparately_DefaultsToTrue()
    {
        Assert.True(new MainViewModel().ShowCoverSeparately);
    }

    [Fact]
    public void ViewMode_CanChange()
    {
        var vm = new MainViewModel { ViewMode = PdfViewMode.Facing };
        Assert.Equal(PdfViewMode.Facing, vm.ViewMode);
    }

    [Fact]
    public void FirstPageCommand_GoesToPageOne()
    {
        var vm = new MainViewModel { TotalPages = 10, CurrentPage = 7 };
        vm.FirstPageCommand.Execute(null);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public void LastPageCommand_GoesToLastPage()
    {
        var vm = new MainViewModel { TotalPages = 10, CurrentPage = 3 };
        vm.LastPageCommand.Execute(null);
        Assert.Equal(10, vm.CurrentPage);
    }

    [Fact]
    public void ShowLibraryViewCommand_SetsShowLibraryTrue()
    {
        var vm = new MainViewModel();
        vm.ShowLibrary = false;
        vm.ShowLibraryViewCommand.Execute(null);
        Assert.True(vm.ShowLibrary);
    }

    [Fact]
    public void ShowLibrary_DefaultsTrue()
    {
        Assert.True(new MainViewModel().ShowLibrary);
    }

    [Fact]
    public void ChatColumn_DefaultsHidden_WhenLibraryShown()
    {
        var vm = new MainViewModel(); // ShowLibrary mặc định true
        Assert.Equal(0, vm.ChatColumnWidth.Value);
        Assert.Equal(0, vm.ChatColumnMinWidth);
    }

    [Fact]
    public void ChatColumn_RestoresDefaultWidth_WhenLeavingLibrary()
    {
        var vm = new MainViewModel();
        vm.ShowLibrary = false;
        Assert.Equal(350, vm.ChatColumnWidth.Value);
        Assert.Equal(280, vm.ChatColumnMinWidth);
    }

    [Fact]
    public void ChatColumn_RemembersResizedWidth_WithinSession()
    {
        var vm = new MainViewModel();
        vm.ShowLibrary = false;                          // hiện panel: 350
        vm.ChatColumnWidth = new GridLength(500);        // mô phỏng kéo GridSplitter
        vm.ShowLibrary = true;                           // vào thư viện: lưu 500, thu về 0
        Assert.Equal(0, vm.ChatColumnWidth.Value);
        vm.ShowLibrary = false;                          // rời thư viện: khôi phục 500
        Assert.Equal(500, vm.ChatColumnWidth.Value);
    }

    // --- Workspace wiring tests (Step 4) ---

    [Fact]
    public void CreateWorkspaceCommand_EmptyName_DoesNotAddWorkspace()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        vm.CreateWorkspaceCommand.Execute("   ");

        Assert.Empty(vm.Workspaces);
    }

    [Fact]
    public void CreateWorkspaceCommand_NullName_DoesNotAddWorkspace()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        vm.CreateWorkspaceCommand.Execute(null);

        Assert.Empty(vm.Workspaces);
    }

    [Fact]
    public void CreateWorkspaceCommand_EmptyName_SetsError()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        vm.CreateWorkspaceCommand.Execute("   ");

        Assert.False(string.IsNullOrEmpty(vm.WorkspaceNameError));
    }

    [Fact]
    public void CreateWorkspaceCommand_ValidName_ClearsError()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);
        vm.CreateWorkspaceCommand.Execute("");          // gây lỗi trước
        Assert.False(string.IsNullOrEmpty(vm.WorkspaceNameError));

        vm.CreateWorkspaceCommand.Execute("Dự án A");   // hợp lệ -> xóa lỗi

        Assert.Equal(string.Empty, vm.WorkspaceNameError);
    }

    [Fact]
    public void CreateWorkspaceCommand_ValidName_AddsToWorkspacesCollection()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        vm.CreateWorkspaceCommand.Execute("Dự án A");

        Assert.Single(vm.Workspaces);
        Assert.Equal("Dự án A", vm.Workspaces[0].Name);
        Assert.False(vm.Workspaces[0].IsDefault);
    }

    [Fact]
    public void CreateWorkspaceCommand_ValidName_PersistsToStore()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        vm.CreateWorkspaceCommand.Execute("Nghiên cứu");

        // Store phải có workspace (IsDefault=false) vừa tạo
        var userWs = wsStore.GetAll(includeDefault: false);
        Assert.Single(userWs);
        Assert.Equal("Nghiên cứu", userWs[0].Name);
    }

    [Fact]
    public void ShowWorkspacesViewCommand_SetsShowWorkspacesTrue()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);
        vm.ShowWorkspaces = false;

        vm.ShowWorkspacesViewCommand.Execute(null);

        Assert.True(vm.ShowWorkspaces);
    }

}
