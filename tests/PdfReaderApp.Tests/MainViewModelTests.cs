using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;
using System.Windows;

namespace PdfReaderApp.Tests;

public class MainViewModelTests
{
    private sealed class FakeChatHistoryStore : PdfReaderApp.Services.IChatHistoryStore
    {
        public readonly List<string> Deleted = [];
        public readonly List<(string doc, string role, string content)> Appended = [];
        private readonly List<PdfReaderApp.Models.ChatHistoryEntry> _entries = [];

        public void EnsureSchema() { }
        public void Append(string documentId, string role, string content, long createdAtUnix, IReadOnlyList<PdfReaderApp.Models.RagSource>? sources = null)
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
        public readonly List<PdfReaderApp.Models.Workspace> All = [];
        // S2: theo dõi membership (wsId -> docIds)
        public readonly Dictionary<string, HashSet<string>> Membership = [];

        public void EnsureSchema() { }
        public void Upsert(PdfReaderApp.Models.Workspace w) { All.RemoveAll(x => x.Id == w.Id); All.Add(w); }
        public PdfReaderApp.Models.Workspace? Get(string id) => All.FirstOrDefault(w => w.Id == id);
        public IReadOnlyList<PdfReaderApp.Models.Workspace> GetAll(bool includeDefault)
            => All.Where(w => includeDefault || !w.IsDefault).ToList();
        public void AddDocument(string workspaceId, string documentId)
        {
            if (!Membership.TryGetValue(workspaceId, out var s)) { s = []; Membership[workspaceId] = s; }
            s.Add(documentId);
        }
        public void RemoveDocument(string workspaceId, string documentId)
        {
            if (Membership.TryGetValue(workspaceId, out var s)) s.Remove(documentId);
        }
        public IReadOnlyList<string> GetDocumentIds(string workspaceId)
            => Membership.TryGetValue(workspaceId, out var s) ? s.ToList() : [];
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
        public readonly System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<PdfReaderApp.Models.OpenTabState>> OpenSets = [];

        public void SaveOpenTabs(string workspaceId, IReadOnlyList<PdfReaderApp.Models.OpenTabState> tabs)
            => OpenSets[workspaceId] = tabs.OrderBy(t => t.TabOrder).ToList();

        public IReadOnlyList<PdfReaderApp.Models.OpenTabState> GetOpenTabs(string workspaceId)
            => OpenSets.TryGetValue(workspaceId, out var s) ? s.ToList() : [];
    }

    private sealed class FakeNoteStore : PdfReaderApp.Services.INoteStore
    {
        public readonly List<PdfReaderApp.Models.Note> Rows = [];
        public bool FailWrites { get; set; }
        public void EnsureSchema() { }
        public void Add(PdfReaderApp.Models.Note note)
        {
            if (FailWrites) throw new InvalidOperationException("Lỗi giả lập khi ghi");
            Rows.Add(note);
        }
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
            new PdfReaderApp.Services.AiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.AiEmbeddingGeneratorFactory(),
            store);

    private static MainViewModel VmWithWorkspaceStore(FakeWorkspaceStore wsStore)
        => new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.AiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.AiEmbeddingGeneratorFactory(),
            workspaceStore: wsStore);

    private static MainViewModel ViewModelAtPage(int totalPages, int currentPage)
    {
        var viewModel = new MainViewModel();
        viewModel.Session.TotalPages = totalPages;
        viewModel.Session.CurrentPage = currentPage;
        return viewModel;
    }

    private static MainViewModel ViewModelWithZoom(double zoomLevel)
    {
        var viewModel = new MainViewModel();
        viewModel.Session.ZoomLevel = zoomLevel;
        return viewModel;
    }

    private static MainViewModel VmWith(FakeWorkspaceStore wsStore, FakeNoteStore notes)
        => new MainViewModel(
            new PdfReaderApp.Services.ITextPdfDocumentService(),
            new PdfReaderApp.Services.WindowsSettingsService(),
            new PdfReaderApp.Services.AiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.AiEmbeddingGeneratorFactory(),
            noteStore: notes,
            workspaceStore: wsStore);

    [Fact]
    public void ReloadWorkspaces_EmptyWorkspace_CardIsEmptyWithZeroCountLabel()
    {
        var wsStore = new FakeWorkspaceStore();
        wsStore.Upsert(new PdfReaderApp.Models.Workspace("ws1", "Dự án", false, null, 1, 1));
        var vm = VmWithWorkspaceStore(wsStore);

        vm.ReloadWorkspaces();

        var card = Assert.Single(vm.Workspaces);
        Assert.Equal("Dự án", card.Name);
        Assert.True(card.IsEmpty);
        Assert.Equal("0 tài liệu", card.Label);
        Assert.Null(card.ActiveTitle);
    }

    [Fact]
    public void ReloadWorkspaces_WithSavedActiveTab_CardShowsActiveDocumentTitle()
    {
        var wsStore = new FakeWorkspaceStore();
        wsStore.Upsert(new PdfReaderApp.Models.Workspace("ws1", "Dự án", false, null, 1, 1));
        wsStore.AddDocument("ws1", "docA");
        wsStore.SaveOpenTabs("ws1", new[]
        {
            new PdfReaderApp.Models.OpenTabState("docA", 0, true, 1, 1.0, 0, 1)
        });
        var vm = VmWithWorkspaceStore(wsStore);
        vm.Library.Add(new PdfReaderApp.Models.LibraryItem("docA", "Tài liệu A", "/a.pdf", null, 10, 1, 1));

        vm.ReloadWorkspaces();

        var card = Assert.Single(vm.Workspaces);
        Assert.False(card.IsEmpty);
        Assert.Equal("1 tài liệu", card.Label);
        Assert.Equal("Tài liệu A", card.ActiveTitle);
        Assert.True(card.HasActiveTitle);
    }

    [Fact]
    public void RemoveLibraryItem_DeletesChatHistoryForThatDocument()
    {
        var store = new FakeChatHistoryStore();
        var vm = VmWithChatStore(store);
        var item = new PdfReaderApp.Models.LibraryItem("docX", "x.pdf", "/lib/x.pdf", null, 3, 1, 1);
        vm.Library.Add(item);

        vm.RemoveLibraryItemCommand.Execute(item);

        Assert.Contains("docX", store.Deleted, StringComparer.Ordinal);
    }

    [Fact]
    public void MemoryTurns_ExcludesLegacyErrorTextsStoredByOlderVersions()
    {
        var turns = new (string role, string content)[]
        {
            ("AI", "Chưa cấu hình API key. Vui lòng mở Cài đặt để nhập OpenAI API key."),
            ("AI", "Nhà cung cấp AI đã chọn chưa được hỗ trợ. Vui lòng chọn OpenAI hoặc Claude trong Cài đặt."),
            ("AI", "Đã xảy ra lỗi khi gọi AI."),
            ("AI", "Trả lời thật"),
        };

        var kept = ChatViewModel.MemoryTurns(turns).ToList();

        Assert.Equal([("AI", "Trả lời thật")], kept);
    }

    [Fact]
    public void MemoryTurns_ExcludesErrorEmptyAndInterruptedAiTurns_KeepsRest()
    {
        var turns = new (string role, string content)[]
        {
            ("User", "Câu hỏi 1"),
            ("AI", "Trả lời tốt"),
            ("User", "Câu hỏi 2"),
            ("AI", "Chưa có API key cho OpenAI. Mở Cài đặt AI để nhập key."),
            ("AI", "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng."),
            ("AI", "Một phần trả lời" + PdfReaderApp.Services.AiChatService.InterruptedSentinel),
            ("AI", ""),
            ("User", "Câu hỏi 3"),
        };

        var kept = ChatViewModel.MemoryTurns(turns).ToList();

        // Giữ: mọi lượt User + lượt AI là câu trả lời thật.
        Assert.Contains(kept, t => t.content == "Câu hỏi 1");
        Assert.Contains(kept, t => t.content == "Câu hỏi 3");
        Assert.Contains(kept, t => t.content == "Trả lời tốt");
        // Bỏ: AI báo lỗi (kể cả lỗi suy ra từ MapError), AI rỗng, AI gián đoạn.
        Assert.DoesNotContain(kept, t => t.content.Contains("Chưa có API key"));
        Assert.DoesNotContain(kept, t => t.content.Contains("Không kết nối được"));
        Assert.DoesNotContain(kept, t => t.content.Contains(PdfReaderApp.Services.AiChatService.InterruptedSentinel));
        Assert.DoesNotContain(kept, t => t.role == "AI" && t.content.Length == 0);
    }


    [Fact]
    public void MainViewModel_ShouldInitializeWithDefaultValues()
    {
        var viewModel = new MainViewModel();
        Assert.Equal("Trí Thư", viewModel.WindowTitle);
        Assert.Equal(1, viewModel.Session.CurrentPage);
        Assert.Equal(1, viewModel.Session.TotalPages);
        Assert.Equal(1.0, viewModel.Session.ZoomLevel);
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
        var viewModel = ViewModelAtPage(5, 1);
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(2, viewModel.Session.CurrentPage);
    }

    [Fact]
    public void NextPageCommand_ShouldNotIncrementPage_WhenAtLastPage()
    {
        var viewModel = ViewModelAtPage(5, 5);
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(5, viewModel.Session.CurrentPage);
    }

    [Fact]
    public void PreviousPageCommand_ShouldDecrementPage_WhenNotAtFirstPage()
    {
        var viewModel = ViewModelAtPage(5, 2);
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.Session.CurrentPage);
    }

    [Fact]
    public void PreviousPageCommand_ShouldNotDecrementPage_WhenAtFirstPage()
    {
        var viewModel = ViewModelAtPage(5, 1);
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.Session.CurrentPage);
    }

    [Fact]
    public void ZoomInCommand_ShouldIncreaseZoomLevel()
    {
        var viewModel = ViewModelWithZoom(1.0);
        viewModel.ZoomInCommand.Execute(null);
        Assert.Equal(1.2, viewModel.Session.ZoomLevel, 1);
    }

    [Fact]
    public void ZoomOutCommand_ShouldDecreaseZoomLevel_WhenAboveMinimum()
    {
        var viewModel = ViewModelWithZoom(1.0);
        viewModel.ZoomOutCommand.Execute(null);
        Assert.Equal(0.8, viewModel.Session.ZoomLevel, 1);
    }

    [Fact]
    public void ZoomOutCommand_ShouldNotDecreaseZoomLevel_WhenAtMinimum()
    {
        var viewModel = ViewModelWithZoom(0.4);
        viewModel.ZoomOutCommand.Execute(null);
        Assert.Equal(0.4, viewModel.Session.ZoomLevel, 1);
    }

    [Fact]
    public void OnSearchQueryChanged_ClearsPageHighlight()
    {
        var vm = new MainViewModel();
        vm.Search.SelectedQuery = "abc";
        vm.Search.Query = "x";
        Assert.Equal(string.Empty, vm.Search.SelectedQuery);
    }

    [Fact]
    public void SearchQuery_SetEmpty_ClearsResultsAndExecutedQuery()
    {
        var vm = new MainViewModel();
        vm.Search.Query = "abc";
        vm.Search.ExecutedQuery = "abc";
        vm.Search.Query = "";
        Assert.Empty(vm.Search.Results);
        Assert.Equal(string.Empty, vm.Search.ExecutedQuery);
    }

    [Fact]
    public void ClearSearchCommand_ClearsQueryAndResults()
    {
        var vm = new MainViewModel();
        vm.Search.Query = "abc";
        vm.Search.ClearCommand.Execute(null);
        Assert.Equal(string.Empty, vm.Search.Query);
        Assert.Empty(vm.Search.Results);
    }

    [Fact]
    public void SelectSearchResult_SetsPageAndHighlightQuery()
    {
        var vm = new MainViewModel();
        vm.Search.Query = "hành";
        var result = new SearchResult(2, "snip", 1);
        vm.Search.SelectResultCommand.Execute(result);
        Assert.Equal(3, vm.Session.CurrentPage);
        Assert.Equal("hành", vm.Search.SelectedQuery);
    }

    [Fact]
    public void ActiveTabChange_ClearsSearchQueryAndHighlight()
    {
        var vm = new MainViewModel();
        vm.Search.Query = "abc";
        vm.Search.SelectedQuery = "abc";
        vm.Search.ExecutedQuery = "abc";

        vm.Session.Tabs.OpenOrActivate("docB", "B", "/lib/b.pdf", initialPage: 1);

        Assert.Equal(string.Empty, vm.Search.Query);
        Assert.Equal(string.Empty, vm.Search.SelectedQuery);
        Assert.Equal(string.Empty, vm.Search.ExecutedQuery);
        Assert.Empty(vm.Search.Results);
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
        var vm = ViewModelAtPage(10, 7);
        vm.FirstPageCommand.Execute(null);
        Assert.Equal(1, vm.Session.CurrentPage);
    }

    [Fact]
    public void LastPageCommand_GoesToLastPage()
    {
        var vm = ViewModelAtPage(10, 3);
        vm.LastPageCommand.Execute(null);
        Assert.Equal(10, vm.Session.CurrentPage);
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
        Assert.False(vm.Workspaces[0].Workspace.IsDefault);
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

        // S2: khi workspace có tài liệu, OpenWorkspace đi thẳng vào phiên đọc (LeaveWorkspacesScreen).
        // ShowWorkspaces = false.
        Assert.False(vm.ShowWorkspaces);
        Assert.Equal(W, vm.SelectedWorkspace);
        Assert.Contains(vm.DocumentsSurface.Members, i => i.DocumentId == "docA");
        Assert.Equal(W.Id, vm.Session.ActiveWorkspaceId);
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
        Assert.Equal(W.Id, vm.Session.ActiveWorkspaceId);
        Assert.True(vm.IsReadingDocument);

        // Dọn dẹp
#pragma warning disable CA1031 // dọn thư mục tạm của test là best-effort
        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
#pragma warning restore CA1031
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

        Assert.Equal(itemB.DocumentId, vm.Session.DocumentId); // đã mở docB
        Assert.Equal(3, vm.Session.CurrentPage);                       // mở thẳng tại trang neo (index 2 -> trang 3)

#pragma warning disable CA1031 // dọn thư mục tạm của test là best-effort
        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
#pragma warning restore CA1031
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

        Assert.Null(vm.Session.ActiveWorkspaceId);       // scope active đã reset
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
        vm.DocumentsSurface.RemoveMemberCommand.Execute(itemA);

        // Note vẫn còn (không bị xóa khi gỡ tài liệu khỏi workspace)
        Assert.Single(notes.GetForOwner(W.Id));
        Assert.Equal("note neo", notes.GetForOwner(W.Id)[0].Content);
        // Membership docA-W đã gỡ
        Assert.DoesNotContain("docA", wsStore.GetDocumentIds(W.Id), StringComparer.Ordinal);
    }

    // ===== #63 Right-panel collapse (global, toggle -> thin strip) =====

    [Fact]
    public void ToggleRightPanel_WhenReadingAndExpanded_CollapsesToThinStrip()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        vm.ShowLibrary = false;   // vào chế độ đọc -> panel mở rộng

        vm.ToggleRightPanelCommand.Execute(null);

        Assert.True(vm.IsRightPanelCollapsed);
        Assert.True(vm.IsRightPanelStripVisible);
        Assert.False(vm.IsRightPanelExpanded);
        Assert.Equal(48d, vm.ChatColumnWidth.Value);
    }

    [Fact]
    public void ToggleRightPanel_TwiceAfterResize_RestoresPreviousWidth()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        vm.ShowLibrary = false;
        vm.ChatColumnWidth = new GridLength(420);   // giả lập người dùng kéo splitter

        vm.ToggleRightPanelCommand.Execute(null);   // thu gọn
        vm.ToggleRightPanelCommand.Execute(null);   // mở lại

        Assert.True(vm.IsRightPanelExpanded);
        Assert.Equal(420d, vm.ChatColumnWidth.Value);
    }

    [Fact]
    public void RightPanelCollapsed_IsGlobal_SurvivesNavigationAwayAndBack()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());
        vm.ShowLibrary = false;
        vm.ToggleRightPanelCommand.Execute(null);     // thu gọn khi đang đọc

        vm.ShowLibrary = true;                          // sang Thư viện
        Assert.False(vm.IsRightPanelStripVisible);      // ẩn hẳn ở Thư viện, không phải dải

        vm.ShowLibrary = false;                         // quay lại đọc
        Assert.True(vm.IsRightPanelCollapsed);          // vẫn thu gọn (global, không reset)
        Assert.True(vm.IsRightPanelStripVisible);
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
            new PdfReaderApp.Services.AiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(TempDb(),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new PdfReaderApp.Services.AiEmbeddingGeneratorFactory(),
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
        Assert.DoesNotContain(notes.Rows, n => string.Equals(n.DocumentId, "docA", StringComparison.Ordinal));
        // Chat history đã bị xóa
        Assert.Contains("docA", chat.Deleted, StringComparer.Ordinal);
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

        Assert.Equal(W.Id, vm.Session.ActiveWorkspaceId);
        // Highlight của default workspace KHÔNG được lẫn vào
        Assert.DoesNotContain(vm.Notes.Highlights, n => n.OwnerKey == Dft.Id);
        Assert.Empty(vm.Notes.Highlights); // W chưa có note

#pragma warning disable CA1031 // dọn thư mục tạm của test là best-effort
        try { System.IO.Directory.Delete(tmpDir, recursive: true); } catch { }
#pragma warning restore CA1031
    }

    [Fact]
    public void NotifySuccess_SetsNotificationsCurrentAsNonError()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());

        vm.NotifySuccess("Đã lưu vào ghi chú");

        Assert.NotNull(vm.Notifications.Current);
        Assert.Equal("Đã lưu vào ghi chú", vm.Notifications.Current!.Message);
        Assert.False(vm.Notifications.Current.IsError);
    }

    [Fact]
    public void SaveAnswerAsNote_StoreFailsWithOpenOwner_ShowsSaveFailedNotNoDocument()
    {
        var notes = new FakeNoteStore();
        var vm = VmWith(new FakeWorkspaceStore(), notes);
        vm.Notes.LoadFor("doc1");
        notes.FailWrites = true;

        vm.SaveAnswerAsNoteCommand.Execute(new ChatMessage { Role = "AI", Content = "Câu trả lời" });

        Assert.NotNull(vm.Notifications.Current);
        Assert.True(vm.Notifications.Current!.IsError);
        Assert.Equal("Không lưu được ghi chú.", vm.Notifications.Current.Message);
    }

    [Fact]
    public void NotifyError_SetsNotificationsCurrentAsError()
    {
        var vm = VmWithWorkspaceStore(new FakeWorkspaceStore());

        vm.NotifyError("Không thể mở file PDF");

        Assert.NotNull(vm.Notifications.Current);
        Assert.Equal("Không thể mở file PDF", vm.Notifications.Current!.Message);
        Assert.True(vm.Notifications.Current.IsError);
    }

}
