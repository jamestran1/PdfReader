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
        // S2: theo dõi membership (wsId -> docIds)
        public readonly Dictionary<string, HashSet<string>> Membership = new();

        public void EnsureSchema() { }
        public void Upsert(PdfReaderApp.Models.Workspace w) { All.RemoveAll(x => x.Id == w.Id); All.Add(w); }
        public PdfReaderApp.Models.Workspace? Get(string id) => All.FirstOrDefault(w => w.Id == id);
        public IReadOnlyList<PdfReaderApp.Models.Workspace> GetAll(bool includeDefault)
            => All.Where(w => includeDefault || !w.IsDefault).ToList();
        public void AddDocument(string workspaceId, string documentId)
        {
            if (!Membership.TryGetValue(workspaceId, out var s)) { s = new(); Membership[workspaceId] = s; }
            s.Add(documentId);
        }
        public void RemoveDocument(string workspaceId, string documentId)
        {
            if (Membership.TryGetValue(workspaceId, out var s)) s.Remove(documentId);
        }
        public IReadOnlyList<string> GetDocumentIds(string workspaceId)
            => Membership.TryGetValue(workspaceId, out var s) ? s.ToList() : new List<string>();
        public IReadOnlyList<string> GetWorkspaceIdsForDocument(string documentId)
        {
            var result = new List<string>();
            foreach (var kv in Membership)
                if (kv.Value.Contains(documentId)) result.Add(kv.Key);
            return result;
        }
        public PdfReaderApp.Models.Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs)
        {
            var existing = All.FirstOrDefault(w => w.IsDefault && w.DefaultDocumentId == documentId);
            if (existing != null) return existing;
            var ws = new PdfReaderApp.Models.Workspace(System.Guid.NewGuid().ToString("N"), name, true, documentId, nowUnixMs, nowUnixMs);
            All.Add(ws);
            AddDocument(ws.Id, documentId);
            return ws;
        }
        public void Rename(string id, string name, long nowUnixMs)
        {
            int i = All.FindIndex(w => w.Id == id);
            if (i >= 0) All[i] = All[i] with { Name = name, UpdatedAtUnixMs = nowUnixMs };
        }
        public void Delete(string id)
        {
            All.RemoveAll(w => w.Id == id);
            Membership.Remove(id);
        }
        public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PdfReaderApp.Models.OpenTabState>> OpenSets = new();

        public void SaveOpenTabs(string workspaceId, IReadOnlyList<PdfReaderApp.Models.OpenTabState> tabs)
            => OpenSets[workspaceId] = tabs.OrderBy(t => t.TabOrder).ToList();

        public IReadOnlyList<PdfReaderApp.Models.OpenTabState> GetOpenTabs(string workspaceId)
            => OpenSets.TryGetValue(workspaceId, out var s) ? s.ToList() : new List<PdfReaderApp.Models.OpenTabState>();
    }

    private sealed class FakeNoteStore : PdfReaderApp.Services.INoteStore
    {
        public readonly List<PdfReaderApp.Models.Note> Rows = new();
        public void EnsureSchema() { }
        public void Add(PdfReaderApp.Models.Note note) => Rows.Add(note);
        public int Update(string id, string content, long now)
        {
            int i = Rows.FindIndex(n => n.Id == id);
            if (i < 0) return 0;
            Rows[i] = Rows[i] with { Content = content, UpdatedAtUnixMs = now };
            return 1;
        }
        public int Delete(string id) => Rows.RemoveAll(n => n.Id == id);
        public IReadOnlyList<PdfReaderApp.Models.Note> GetForOwner(string ownerKey)
            => Rows.Where(n => n.OwnerKey == ownerKey).ToList();
        public int ReassignOwner(string oldKey, string newKey)
        {
            int count = 0;
            for (int i = 0; i < Rows.Count; i++)
            {
                if (Rows[i].OwnerKey == oldKey) { Rows[i] = Rows[i] with { OwnerKey = newKey }; count++; }
            }
            return count;
        }
        public int DeleteForOwner(string ownerKey) => Rows.RemoveAll(n => n.OwnerKey == ownerKey);
        public int DeleteForDocument(string documentId) => Rows.RemoveAll(n => n.DocumentId == documentId);
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

    private static MainViewModel VmWith(FakeWorkspaceStore wsStore, FakeNoteStore notes)
        => new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.OpenAiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.OpenAiEmbeddingGeneratorFactory(),
            noteStore: notes,
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
        Assert.Equal("Trí Thư", viewModel.WindowTitle);
        Assert.Equal(1, viewModel.CurrentPage);
        Assert.Equal(1, viewModel.TotalPages);
        Assert.Equal(1.0, viewModel.ZoomLevel);
    }

    [Fact]
    public void WindowTitle_Defaults_ToTriThu()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        Assert.Equal("Trí Thư", vm.WindowTitle);
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

    // --- S2 tests (#34) ---

    [Fact]
    public void OpenWorkspace_LoadsDocuments_AndShowsDetail()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        // Tạo workspace W (không phải default)
        var W = new PdfReaderApp.Models.Workspace("ws-A", "Dự án A", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");

        // Thêm LibraryItem docA vào Library
        var itemA = new PdfReaderApp.Models.LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 0, 1, 1);
        vm.Library.Add(itemA);

        vm.OpenWorkspaceCommand.Execute(W);

        // S2: khi workspace có tài liệu, OpenWorkspace đi thẳng vào phiên đọc (EnterReadingSession).
        // ShowWorkspaceDetail = false (tắt màn quản lý), ShowWorkspaces = false.
        Assert.False(vm.ShowWorkspaceDetail);
        Assert.Equal(W, vm.SelectedWorkspace);
        Assert.Contains(vm.WorkspaceDocuments, i => i.DocumentId == "docA");
        Assert.Equal(W.Id, vm.ActiveWorkspaceId);
        Assert.False(vm.ShowWorkspacesGrid);
    }

    [Fact]
    public void AddDocumentsToWorkspace_AddsMembershipAndRefreshes()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        var W = new PdfReaderApp.Models.Workspace("ws-B", "Dự án B", false, null, 1, 1);
        wsStore.Upsert(W);

        var itemA = new PdfReaderApp.Models.LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 0, 1, 1);
        var itemB = new PdfReaderApp.Models.LibraryItem("docB", "Sách B", "/lib/b.pdf", null, 0, 1, 1);
        vm.Library.Add(itemA);
        vm.Library.Add(itemB);

        // Mở workspace W (rỗng)
        vm.OpenWorkspaceCommand.Execute(W);

        // Thêm docA và docB
        vm.AddDocumentsToWorkspaceCommand.Execute(
            new System.Collections.Generic.List<object> { itemA, itemB });

        Assert.Contains("docA", wsStore.GetDocumentIds(W.Id));
        Assert.Contains("docB", wsStore.GetDocumentIds(W.Id));
        Assert.Equal(2, vm.WorkspaceDocuments.Count);
    }

    [Fact]
    public void RemoveDocumentFromWorkspace_RemovesMembershipAndRefreshes()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        var W = new PdfReaderApp.Models.Workspace("ws-C", "Dự án C", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");
        wsStore.AddDocument(W.Id, "docB");

        var itemA = new PdfReaderApp.Models.LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 0, 1, 1);
        var itemB = new PdfReaderApp.Models.LibraryItem("docB", "Sách B", "/lib/b.pdf", null, 0, 1, 1);
        vm.Library.Add(itemA);
        vm.Library.Add(itemB);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.RemoveDocumentFromWorkspaceCommand.Execute(itemA);

        Assert.DoesNotContain("docA", wsStore.GetDocumentIds(W.Id));
        Assert.Single(vm.WorkspaceDocuments);
        Assert.Equal("docB", vm.WorkspaceDocuments[0].DocumentId);
    }

    [Fact]
    public void ResolveWorkspaceScope_ExplicitWorkspace_ReturnsThatId()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        var result = vm.ResolveWorkspaceScope("ws-1", "docA", "A", 1);

        Assert.Equal("ws-1", result);
    }

    [Fact]
    public void ResolveWorkspaceScope_NullExplicit_ReturnsDefaultWorkspaceId()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        var id1 = vm.ResolveWorkspaceScope(null, "docA", "A", 1);
        // Không truyền workspace tường minh -> dùng default workspace của tài liệu
        var def = wsStore.All.Single(w => w.IsDefault && w.DefaultDocumentId == "docA");
        Assert.Equal(def.Id, id1);

        // Idempotent: gọi lần hai cùng docId trả về cùng id
        var id2 = vm.ResolveWorkspaceScope(null, "docA", "A", 2);
        Assert.Equal(id1, id2);
    }

    [Fact]
    public void BackToWorkspaceList_ShowsGridAgain()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        var W = new PdfReaderApp.Models.Workspace("ws-D", "Dự án D", false, null, 1, 1);
        wsStore.Upsert(W);
        vm.OpenWorkspaceCommand.Execute(W);
        Assert.True(vm.ShowWorkspaceDetail);

        vm.BackToWorkspaceListCommand.Execute(null);

        Assert.False(vm.ShowWorkspaceDetail);
        Assert.True(vm.ShowWorkspacesGrid);
    }

    [Fact]
    public void OpenWorkspaceDocument_SetsActiveWorkspaceToWorkspaceId()
    {
        // Test 7: integration dùng PDF thật (tạo bằng iText)
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        // Tạo PDF tạm bằng iText
        var tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        var pdfPath = System.IO.Path.Combine(tmpDir, "ws_doc.pdf");
        using (var writer = new iText.Kernel.Pdf.PdfWriter(pdfPath))
        using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(writer))
        using (var doc = new iText.Layout.Document(pdfDoc))
        {
            doc.Add(new iText.Layout.Element.Paragraph("Nội dung kiểm thử workspace S2"));
        }

        var W = new PdfReaderApp.Models.Workspace("ws-E", "Dự án E", false, null, 1, 1);
        wsStore.Upsert(W);

        var item = new PdfReaderApp.Models.LibraryItem(
            PdfReaderApp.Services.DocumentId.FromFile(pdfPath),
            "ws_doc", pdfPath, null, 0, 1, 1);
        vm.Library.Add(item);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(item);

        // active workspace phải là W.Id (không phải default)
        Assert.Equal(W.Id, vm.ActiveWorkspaceId);
        Assert.True(vm.IsReadingDocument);

        // Dọn dẹp
        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
    }

    [Fact]
    // Guard hợp đồng VM: bấm note cross-doc mở đúng tài liệu VÀ đặt CurrentPage = trang neo.
    // LƯU Ý: test này KHÔNG phủ phần timing của PdfViewerControl (reset trang khi nạp) vì không có
    // WPF control trong unit test; phần đó phải verify GUI. Nó chặn hồi quy việc đánh rơi/sai trang ở VM.
    public void OpenNoteOfAnotherDocument_OpensThatDocument_AtAnchoredPage()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = VmWithWorkspaceStore(wsStore);

        var tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        string MakePdf(string name, int pages)
        {
            var p = System.IO.Path.Combine(tmpDir, name);
            using var writer = new iText.Kernel.Pdf.PdfWriter(p);
            using var pdfDoc = new iText.Kernel.Pdf.PdfDocument(writer);
            using var doc = new iText.Layout.Document(pdfDoc);
            for (int i = 0; i < pages; i++)
            {
                doc.Add(new iText.Layout.Element.Paragraph($"Trang {i + 1}"));
                if (i < pages - 1) doc.Add(new iText.Layout.Element.AreaBreak());
            }
            return p;
        }

        var pathA = MakePdf("docA.pdf", 1);
        var pathB = MakePdf("docB.pdf", 3);
        var W = new PdfReaderApp.Models.Workspace("ws-F", "Dự án F", false, null, 1, 1);
        wsStore.Upsert(W);

        var itemA = new PdfReaderApp.Models.LibraryItem(
            PdfReaderApp.Services.DocumentId.FromFile(pathA), "docA", pathA, null, 0, 1, 1);
        var itemB = new PdfReaderApp.Models.LibraryItem(
            PdfReaderApp.Services.DocumentId.FromFile(pathB), "docB", pathB, null, 0, 1, 1);
        vm.Library.Add(itemA);
        vm.Library.Add(itemB);
        wsStore.AddDocument(W.Id, itemA.DocumentId);
        wsStore.AddDocument(W.Id, itemB.DocumentId);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(itemA); // đang đọc docA

        // note thuộc docB, neo ở trang index 2 (trang số 3)
        var note = new PdfReaderApp.Models.Note("n1", W.Id, itemB.DocumentId, 2, null, "ghi chú docB", 1, 1);
        vm.Notes.OpenCommand.Execute(note);

        Assert.Equal(itemB.DocumentId, vm.CurrentDocumentId); // đã mở docB
        Assert.Equal(3, vm.CurrentPage);                       // mở thẳng tại trang neo (index 2 -> trang 3)

        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
    }

    // --- S4 tests (#36) ---

    [Fact]
    public void DeleteWorkspace_DeletesItsNotes_AndRemovesWorkspace()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var W = new PdfReaderApp.Models.Workspace("ws-del", "Dự án xóa", false, null, 1, 1);
        wsStore.Upsert(W);
        // Thêm note có owner = W vào fake note store
        notes.Rows.Add(new PdfReaderApp.Models.Note("n1", W.Id, null, null, null, "ghi chú", 1, 1));

        vm.DeleteWorkspaceCommand.Execute(W);

        // Note của workspace đã bị xóa
        Assert.Empty(notes.GetForOwner(W.Id));
        // Workspace đã bị xóa khỏi store
        Assert.Null(wsStore.Get(W.Id));
    }

    [Fact]
    public void DeleteWorkspace_WhenActiveAndOpen_ResetsScopeAndExitsDetail()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var W = new PdfReaderApp.Models.Workspace("ws-active", "Dự án đang mở", false, null, 1, 1);
        wsStore.Upsert(W);
        vm.OpenWorkspaceCommand.Execute(W); // W trở thành active + đang ở màn chi tiết

        vm.DeleteWorkspaceCommand.Execute(W);

        Assert.Null(vm.ActiveWorkspaceId);       // scope active đã reset
        Assert.False(vm.ShowWorkspaceDetail);    // đã thoát màn chi tiết
        Assert.Null(vm.SelectedWorkspace);       // không giữ tham chiếu workspace đã xóa
        Assert.Null(wsStore.Get(W.Id));
    }

    [Fact]
    public void DeleteWorkspace_DefaultWorkspace_IsNotDeleted()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var D = new PdfReaderApp.Models.Workspace("ws-def", "Tài liệu mặc định", true, "docD", 1, 1);
        wsStore.Upsert(D);
        notes.Rows.Add(new PdfReaderApp.Models.Note("n1", D.Id, null, null, null, "ghi chú default", 1, 1));

        vm.DeleteWorkspaceCommand.Execute(D);

        // Default workspace không bị xóa
        Assert.NotNull(wsStore.Get(D.Id));
        // Note của default workspace vẫn còn
        Assert.Single(notes.GetForOwner(D.Id));
    }

    [Fact]
    public void RenameWorkspace_EmptyName_SetsError_AndKeepsName()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var W = new PdfReaderApp.Models.Workspace("ws-ren", "Tên gốc", false, null, 1, 1);
        wsStore.Upsert(W);
        vm.OpenWorkspaceCommand.Execute(W);

        vm.RenameWorkspaceCommand.Execute("  ");

        // Lỗi phải được set
        Assert.False(string.IsNullOrEmpty(vm.WorkspaceNameError));
        // Tên không đổi
        Assert.Equal("Tên gốc", wsStore.Get(W.Id)!.Name);
    }

    [Fact]
    public void RenameWorkspace_ValidName_UpdatesName()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var W = new PdfReaderApp.Models.Workspace("ws-ren2", "Tên cũ", false, null, 1, 1);
        wsStore.Upsert(W);
        vm.OpenWorkspaceCommand.Execute(W);

        vm.RenameWorkspaceCommand.Execute("Tên mới");

        Assert.Equal("Tên mới", wsStore.Get(W.Id)!.Name);
    }

    [Fact]
    public void RemoveDocumentFromWorkspace_KeepsAnchoredNotes()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var W = new PdfReaderApp.Models.Workspace("ws-rem", "Dự án", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");

        // Note neo vào docA trong workspace W
        notes.Rows.Add(new PdfReaderApp.Models.Note("n1", W.Id, "docA", 1, null, "note neo", 1, 1));

        var itemA = new PdfReaderApp.Models.LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 0, 1, 1);
        vm.Library.Add(itemA);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.RemoveDocumentFromWorkspaceCommand.Execute(itemA);

        // Note vẫn còn (không bị xóa khi gỡ tài liệu khỏi workspace)
        Assert.Single(notes.GetForOwner(W.Id));
        Assert.Equal("note neo", notes.GetForOwner(W.Id)[0].Content);
        // Membership docA-W đã gỡ
        Assert.DoesNotContain("docA", wsStore.GetDocumentIds(W.Id));
    }

    [Fact]
    public void RemoveLibraryItem_Cascades_RemovesMembership_DeletesDefaultWs_CleansNotesAndChat()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var chat = new FakeChatHistoryStore();
        // Dùng constructor đầy đủ để inject chat store
        var vm = new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.OpenAiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.OpenAiEmbeddingGeneratorFactory(),
            chatHistory: chat,
            noteStore: notes,
            workspaceStore: wsStore);

        // Default workspace cho docA
        var Dft = new PdfReaderApp.Models.Workspace("ws-dft", "docA default", true, "docA", 1, 1);
        wsStore.Upsert(Dft);
        wsStore.AddDocument(Dft.Id, "docA");

        // Workspace dùng chung W
        var W = new PdfReaderApp.Models.Workspace("ws-shared", "Chia sẻ", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");

        // Note owner=Dft neo docA
        notes.Rows.Add(new PdfReaderApp.Models.Note("n1", Dft.Id, "docA", 0, null, "note default ws", 1, 1));
        // Note owner=W neo docA
        notes.Rows.Add(new PdfReaderApp.Models.Note("n2", W.Id, "docA", 0, null, "note shared ws", 1, 1));

        var itemA = new PdfReaderApp.Models.LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 0, 1, 1);
        vm.Library.Add(itemA);

        vm.RemoveLibraryItemCommand.Execute(itemA);

        // Default workspace Dft phải bị xóa
        Assert.Null(wsStore.Get(Dft.Id));
        // Mọi membership docA phải gỡ sạch
        Assert.Empty(wsStore.GetWorkspaceIdsForDocument("docA"));
        // Không còn note nào neo tới docA
        Assert.Empty(notes.Rows.Where(n => n.DocumentId == "docA"));
        // Chat history đã bị xóa
        Assert.Contains("docA", chat.Deleted);
        // LibraryItem đã bị xóa khỏi vm.Library
        Assert.DoesNotContain(vm.Library, i => i.DocumentId == "docA");
    }

    [Fact]
    // #38: quay lại trình đọc -> tắt cả Thư viện lẫn Workspaces (và màn chi tiết).
    public void ShowReaderView_ExitsLibraryAndWorkspaces()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        vm.ShowWorkspacesViewCommand.Execute(null); // đang ở Workspaces

        vm.ShowReaderViewCommand.Execute(null);

        Assert.False(vm.ShowLibrary);
        Assert.False(vm.ShowWorkspaces);
        Assert.False(vm.ShowWorkspaceDetail);
        Assert.True(vm.IsReadingDocument);
    }

    [Fact]
    public void ActiveNavDestination_DefaultsToLibrary_OnStartup()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        Assert.Equal(NavDestination.Library, vm.ActiveNavDestination);
    }

    [Fact]
    public void ActiveNavDestination_IsWorkspaces_AfterShowWorkspacesView()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        vm.ShowWorkspacesViewCommand.Execute(null);
        Assert.Equal(NavDestination.Workspaces, vm.ActiveNavDestination);
    }

    [Fact]
    public void ActiveNavDestination_IsReader_AfterShowReaderView()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        vm.ShowReaderViewCommand.Execute(null);
        Assert.Equal(NavDestination.Reader, vm.ActiveNavDestination);
    }

    [Fact]
    public void ActiveNavDestination_RaisesPropertyChanged_WhenViewChanges()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.ActiveNavDestination)) raised = true;
        };
        vm.ShowWorkspacesViewCommand.Execute(null);
        Assert.True(raised);
    }

    [Fact]
    // #40 (regression): mở tài liệu trong workspace mới chỉ hiện highlight của workspace đó,
    // KHÔNG lẫn highlight của default workspace (notes scope theo owner_key).
    public void OpenDocumentInNewWorkspace_DoesNotLeakDefaultWorkspaceHighlights()
    {
        var wsStore = new FakeWorkspaceStore();
        var notes = new FakeNoteStore();
        var vm = VmWith(wsStore, notes);

        var tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmpDir);
        var pdfPath = System.IO.Path.Combine(tmpDir, "docA.pdf");
        using (var writer = new iText.Kernel.Pdf.PdfWriter(pdfPath))
        using (var pdfDoc = new iText.Kernel.Pdf.PdfDocument(writer))
        using (var doc = new iText.Layout.Document(pdfDoc))
            doc.Add(new iText.Layout.Element.Paragraph("Nội dung docA"));

        string docId = PdfReaderApp.Services.DocumentId.FromFile(pdfPath);

        // Default workspace của docA + một highlight note (owner = default).
        var Dft = new PdfReaderApp.Models.Workspace("ws-dft", "docA default", true, docId, 1, 1);
        wsStore.Upsert(Dft);
        wsStore.AddDocument(Dft.Id, docId);
        var rects = new System.Collections.Generic.List<PdfReaderApp.Models.HighlightRect>
            { new PdfReaderApp.Models.HighlightRect(10, 10, 50, 12) };
        notes.Rows.Add(new PdfReaderApp.Models.Note("hl-default", Dft.Id, docId, 0, "trích", "note default", 1, 1, rects, "#FFEB3B"));

        // Workspace mới W chứa docA (chưa có note nào).
        var W = new PdfReaderApp.Models.Workspace("ws-new", "Workspace mới", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, docId);

        var itemA = new PdfReaderApp.Models.LibraryItem(docId, "docA", pdfPath, null, 0, 1, 1);
        vm.Library.Add(itemA);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(itemA);

        Assert.Equal(W.Id, vm.ActiveWorkspaceId);
        // Highlight của default workspace KHÔNG được lẫn vào
        Assert.DoesNotContain(vm.Notes.Highlights, n => n.OwnerKey == Dft.Id);
        Assert.Empty(vm.Notes.Highlights); // W chưa có note

        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
    }

}
