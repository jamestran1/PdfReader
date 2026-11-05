using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// TDD cho ActivateTabCommand và CloseTabCommand trên MainViewModel (Tab S1 UI layer).
/// Dùng bộ nạp giả (FakeDocumentLoader) để không nạp PDF thật.
/// </summary>
public class MainViewModelTabCommandTests
{

    private static MainViewModel CreateVm(IWorkspaceStore wsStore)
        => new(
            new ITextPdfDocumentService(),
            new WindowsSettingsService(),
            new AiChatClientFactory(),
            new PdfReaderApp.Services.SqliteDocumentIndex(
                System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db"),
                System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
            new AiEmbeddingGeneratorFactory(),
            workspaceStore: wsStore,
            documentLoader: new PdfReaderApp.Tests.Fakes.FakeDocumentLoader());

    // Đọc lẻ một tài liệu qua Phiên đọc; bộ nạp giả lấy DocumentId bằng chính đường dẫn nên item dùng đường dẫn làm id.
    private static void OpenStandalone(MainViewModel vm, string path, string title, int page, double zoom)
    {
        var item = MakeItem(path, title, path);
        vm.Library.Add(item);
        vm.OpenLibraryItemCommand.Execute(item);
        vm.Session.CurrentPage = page;
        vm.Session.ZoomLevel = zoom;
    }

    private sealed class FakeWorkspaceStore : IWorkspaceStore
    {
        public readonly List<Workspace> All = [];
        public readonly Dictionary<string, HashSet<string>> Membership = [];
        public int RemoveDocumentCallCount { get; private set; }

        public void EnsureSchema() { }
        public void Upsert(Workspace w) { All.RemoveAll(x => x.Id == w.Id); All.Add(w); }
        public Workspace? Get(string id) => All.FirstOrDefault(w => w.Id == id);
        public IReadOnlyList<Workspace> GetAll(bool includeDefault)
            => All.Where(w => includeDefault || !w.IsDefault).ToList();
        public void AddDocument(string workspaceId, string documentId)
        {
            if (!Membership.TryGetValue(workspaceId, out var s)) { s = []; Membership[workspaceId] = s; }
            s.Add(documentId);
        }
        public void RemoveDocument(string workspaceId, string documentId)
        {
            RemoveDocumentCallCount++;
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
        public Workspace GetOrCreateDefaultForDocument(string documentId, string name, long nowUnixMs)
        {
            var existing = All.FirstOrDefault(w => w.IsDefault && w.DefaultDocumentId == documentId);
            if (existing != null) return existing;
            var ws = new Workspace(System.Guid.NewGuid().ToString("N"), name, true, documentId, nowUnixMs, nowUnixMs);
            All.Add(ws);
            AddDocument(ws.Id, documentId);
            return ws;
        }
        public void Rename(string id, string name, long nowUnixMs)
        {
            int i = All.FindIndex(w => w.Id == id);
            if (i >= 0) All[i] = All[i] with { Name = name, UpdatedAtUnixMs = nowUnixMs };
        }
        public void Delete(string id) { All.RemoveAll(w => w.Id == id); Membership.Remove(id); }
        public readonly Dictionary<string, List<OpenTabState>> OpenSets = [];

        public void SaveOpenTabs(string workspaceId, IReadOnlyList<OpenTabState> tabs)
            => OpenSets[workspaceId] = tabs.OrderBy(t => t.TabOrder).ToList();

        public IReadOnlyList<OpenTabState> GetOpenTabs(string workspaceId)
            => OpenSets.TryGetValue(workspaceId, out var s) ? s.ToList() : [];
    }

    private static (MainViewModel vm, FakeWorkspaceStore wsStore) MakeVm()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = CreateVm(wsStore);
        return (vm, wsStore);
    }

    private static LibraryItem MakeItem(string docId, string title = "Tài liệu", string path = "/path/doc.pdf")
        => new LibraryItem(docId, title, path, null, 0, 1, 1);

    // Mở workspace và hai tài liệu -> trả về cả hai tab để test.
    private static (OpenTab tabA, OpenTab tabB) OpenTwoTabs(MainViewModel vm, FakeWorkspaceStore wsStore)
    {
        var W = new Workspace("ws-cmd", "Dự án", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");
        wsStore.AddDocument(W.Id, "docB");

        var itemA = MakeItem("docA", "Tài liệu A", "/path/a.pdf");
        var itemB = MakeItem("docB", "Tài liệu B", "/path/b.pdf");
        vm.Library.Add(itemA);
        vm.Library.Add(itemB);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(itemA);
        vm.OpenWorkspaceDocumentCommand.Execute(itemB);

        return (vm.Session.Tabs.Tabs[0], vm.Session.Tabs.Tabs[1]);
    }

    // =========================================================
    // ActivateTabCommand: kích hoạt đúng tab được truyền vào
    // =========================================================
    [Fact]
    public void ActivateTabCommand_ActivatesGivenTab()
    {
        var (vm, wsStore) = MakeVm();
        var (tabA, tabB) = OpenTwoTabs(vm, wsStore);

        // tabB đang active (mở sau). Kích hoạt lại tabA.
        vm.ActivateTabCommand.Execute(tabA);

        Assert.Equal(tabA, vm.Session.Tabs.ActiveTab);
    }

    // =========================================================
    // ActivateTabCommand: không thay đổi membership workspace
    // =========================================================
    [Fact]
    public void ActivateTabCommand_DoesNotTouchWorkspaceMembership()
    {
        var (vm, wsStore) = MakeVm();
        var (tabA, _) = OpenTwoTabs(vm, wsStore);

        vm.ActivateTabCommand.Execute(tabA);

        Assert.Equal(0, wsStore.RemoveDocumentCallCount);
        Assert.Contains("docA", wsStore.GetDocumentIds("ws-cmd"), StringComparer.Ordinal);
        Assert.Contains("docB", wsStore.GetDocumentIds("ws-cmd"), StringComparer.Ordinal);
    }

    // =========================================================
    // CloseTabCommand: xóa tab khỏi Open Set
    // =========================================================
    [Fact]
    public void CloseTabCommand_RemovesTabFromOpenSet()
    {
        var (vm, wsStore) = MakeVm();
        var (tabA, tabB) = OpenTwoTabs(vm, wsStore);

        vm.CloseTabCommand.Execute(tabB);

        Assert.Single(vm.Session.Tabs.Tabs);
        Assert.DoesNotContain(tabB, vm.Session.Tabs.Tabs);
    }

    // =========================================================
    // CloseTabCommand: KHÔNG gọi RemoveDocument trên workspace store
    // =========================================================
    [Fact]
    public void CloseTabCommand_DoesNotCallRemoveDocumentOnWorkspaceStore()
    {
        var (vm, wsStore) = MakeVm();
        var (_, tabB) = OpenTwoTabs(vm, wsStore);

        vm.CloseTabCommand.Execute(tabB);

        Assert.Equal(0, wsStore.RemoveDocumentCallCount);
        Assert.Contains("docB", wsStore.GetDocumentIds("ws-cmd"), StringComparer.Ordinal);
    }

    // =========================================================
    // CloseTabCommand: đóng tab active -> active chuyển sang tab còn lại
    // =========================================================
    [Fact]
    public void CloseTabCommand_ClosingActiveTab_ActivatesNextTab()
    {
        var (vm, wsStore) = MakeVm();
        var (tabA, tabB) = OpenTwoTabs(vm, wsStore);

        // tabB là active; đóng nó -> tabA trở thành active
        vm.CloseTabCommand.Execute(tabB);

        Assert.Equal(tabA, vm.Session.Tabs.ActiveTab);
    }

    // =========================================================
    // ShowWorkspaceDocumentsCommand: "+" mở Workspace Documents surface (modal DialogHost).
    // =========================================================
    [Fact]
    public void ShowWorkspaceDocumentsCommand_OpensWorkspaceDocumentSurface()
    {
        var (vm, wsStore) = MakeVm();
        OpenTwoTabs(vm, wsStore);

        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        Assert.True(vm.DocumentsSurface.IsModalOpen);
    }

    [Fact]
    public void ShowWorkspaceDocumentsCommand_PreservesOpenSet()
    {
        var (vm, wsStore) = MakeVm();
        OpenTwoTabs(vm, wsStore);

        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        Assert.Equal(2, vm.Session.Tabs.Tabs.Count);
    }

    [Fact]
    public void ShowWorkspaceDocumentsCommand_ThenOpeningDocument_ReturnsToSessionWithoutDuplicateTab()
    {
        var (vm, wsStore) = MakeVm();
        OpenTwoTabs(vm, wsStore);

        vm.ShowWorkspaceDocumentsCommand.Execute(null);
        vm.DocumentsSurface.OpenMemberCommand.Execute(MakeItem("docA", "Tài liệu A", "/path/a.pdf"));

        Assert.True(vm.Session.IsWorkspaceSession);
        Assert.False(vm.DocumentsSurface.IsModalOpen);
        Assert.Equal(2, vm.Session.Tabs.Tabs.Count);                 // không tạo tab trùng
        Assert.Equal("docA", vm.Session.Tabs.ActiveTab!.ItemId);
    }

    // =========================================================
    // #64: Tab Strip tài liệu ẩn ở Thư viện dù phiên workspace còn active.
    // =========================================================
    [Fact]
    public void IsDocumentTabStripVisible_FalseOnLibrary_EvenDuringWorkspaceSession()
    {
        var (vm, wsStore) = MakeVm();
        OpenTwoTabs(vm, wsStore);                 // phiên workspace + đang đọc
        Assert.True(vm.IsDocumentTabStripVisible);

        vm.ShowLibrary = true;                    // điều hướng sang Thư viện

        Assert.True(vm.Session.IsWorkspaceSession);       // phiên vẫn còn
        Assert.False(vm.IsDocumentTabStripVisible);
    }

    // =========================================================
    // #68: promote Default -> named Workspace từ tài liệu đang đọc lẻ
    // =========================================================
    [Fact]
    public void PromoteToWorkspace_FromStandalone_CreatesNamedWorkspaceWithDocFirstTabAndSession()
    {
        var (vm, wsStore) = MakeVm();
        OpenStandalone(vm, "/lib/a.pdf", "Tài liệu A", 7, 1.5);

        vm.PromoteToWorkspaceCommand.Execute("Dự án A");

        Assert.True(vm.Session.IsWorkspaceSession);
        var workspace = Assert.Single(wsStore.All, w => !w.IsDefault);
        Assert.Equal("Dự án A", workspace.Name);
        Assert.Equal(workspace.Id, vm.Session.ActiveWorkspaceId);
        Assert.Contains("/lib/a.pdf", wsStore.GetDocumentIds(workspace.Id), StringComparer.Ordinal);

        var tab = Assert.Single(vm.Session.Tabs.Tabs);
        Assert.Equal("/lib/a.pdf", tab.ItemId);
        Assert.Equal(7, tab.Page);
        Assert.Equal(1.5, tab.Zoom);
    }

    [Fact]
    public void PromoteToWorkspace_EmptyName_SetsErrorAndCreatesNothing()
    {
        var (vm, wsStore) = MakeVm();
        OpenStandalone(vm, "/lib/a.pdf", "Tài liệu A", 1, 1.0);

        vm.PromoteToWorkspaceCommand.Execute("   ");

        Assert.False(vm.Session.IsWorkspaceSession);
        Assert.DoesNotContain(wsStore.All, w => !w.IsDefault);
        Assert.False(string.IsNullOrEmpty(vm.WorkspaceNameError));
    }

    [Fact]
    public void PromoteToWorkspace_WhenAlreadyInWorkspaceSession_DoesNothing()
    {
        var (vm, wsStore) = MakeVm();
        OpenTwoTabs(vm, wsStore);                 // đang ở phiên workspace (IsWorkspaceSession=true)
        int workspaceCountBefore = wsStore.All.Count;

        vm.PromoteToWorkspaceCommand.Execute("Khác");

        Assert.Equal(workspaceCountBefore, wsStore.All.Count);
        Assert.Equal(2, vm.Session.Tabs.Tabs.Count);      // Open Set không đổi
    }

    // #68 bug: sau promote rồi mở surface tài liệu, tài liệu vừa promote phải là member (không rỗng).
    [Fact]
    public void PromoteThenShowWorkspaceDocuments_ListsThePromotedDocumentAsMember()
    {
        var (vm, wsStore) = MakeVm();
        OpenStandalone(vm, "/lib/a.pdf", "Tài liệu A", 3, 1.0);

        vm.PromoteToWorkspaceCommand.Execute("Dự án A");
        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        Assert.Contains(vm.DocumentsSurface.Members, d => d.DocumentId == "/lib/a.pdf");
    }
}
