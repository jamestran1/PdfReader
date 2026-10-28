using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// TDD vertical slices cho phần wiring Tab S1 trong MainViewModel (hành vi 8-10).
/// Dùng bộ nạp giả (FakeDocumentLoader) để không nạp PDF thật.
/// </summary>
public class MainViewModelTabTests
{
    // --- Test seam: no-op HydrateTab ---

    private static MainViewModel NewVm(IWorkspaceStore wsStore, FakeDocumentLoader loader) => new(
        new ITextPdfDocumentService(),
        new WindowsSettingsService(),
        new AiChatClientFactory(),
        new PdfReaderApp.Services.SqliteDocumentIndex(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db"),
            System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
        new AiEmbeddingGeneratorFactory(),
        workspaceStore: wsStore,
        documentLoader: loader);

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

    private static (MainViewModel vm, FakeWorkspaceStore wsStore) MakeVm(FakeDocumentLoader? loader = null)
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = NewVm(wsStore, loader ?? new FakeDocumentLoader());
        return (vm, wsStore);
    }

    private static LibraryItem MakeItem(string docId, string title = "Tài liệu", string path = "/path/doc.pdf")
        => new LibraryItem(docId, title, path, null, 0, 1, 1);

    // =========================================================
    // Hành vi 8: Mở cùng tài liệu workspace hai lần -> đúng một tab;
    //            membership trong FakeWorkspaceStore KHÔNG bị thay đổi bởi mở/kích hoạt.
    // =========================================================
    [Fact]
    public void OpenWorkspaceDocument_Twice_ExactlyOneTab_MembershipUnchanged()
    {
        var (vm, wsStore) = MakeVm();
        var W = new Workspace("ws-1", "Dự án 1", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");

        var item = MakeItem("docA", "Tài liệu A", "/path/a.pdf");
        vm.Library.Add(item);

        // Mở workspace chi tiết
        vm.OpenWorkspaceCommand.Execute(W);

        // Mở cùng tài liệu hai lần
        vm.OpenWorkspaceDocumentCommand.Execute(item);
        vm.OpenWorkspaceDocumentCommand.Execute(item);

        // Chỉ một tab
        Assert.Single(vm.Tabs.Tabs);
        // Membership không bị sửa (AddDocument hay RemoveDocument không được gọi thêm)
        Assert.Contains("docA", wsStore.GetDocumentIds(W.Id), StringComparer.Ordinal);
        Assert.Equal(0, wsStore.RemoveDocumentCallCount);
    }

    // =========================================================
    // Hành vi 9: Đóng tab KHÔNG gọi RemoveDocument trên workspace store (membership nguyên vẹn)
    // =========================================================
    [Fact]
    public void CloseTab_DoesNotCallRemoveDocumentOnWorkspaceStore()
    {
        var (vm, wsStore) = MakeVm();
        var W = new Workspace("ws-2", "Dự án 2", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");

        var item = MakeItem("docA", "Tài liệu A", "/path/a.pdf");
        vm.Library.Add(item);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(item);

        // Đóng tab vừa mở
        var tab = vm.Tabs.Tabs[0];
        vm.Tabs.Close(tab);

        // Membership phải nguyên vẹn - đóng tab != gỡ tài liệu khỏi workspace
        Assert.Equal(0, wsStore.RemoveDocumentCallCount);
        Assert.Contains("docA", wsStore.GetDocumentIds(W.Id), StringComparer.Ordinal);
    }

    // =========================================================
    // Hành vi 10: Standalone open -> IsWorkspaceSession=false; mở lần hai thay thế (không tích lũy tab)
    // =========================================================
    [Fact]
    public void StandaloneOpen_SetsIsWorkspaceSessionFalse_AndSecondOpenReplaces()
    {
        var (vm, wsStore) = MakeVm();

        var itemA = MakeItem("docA", "Tài liệu A", "/path/a.pdf");
        var itemB = MakeItem("docB", "Tài liệu B", "/path/b.pdf");
        vm.Library.Add(itemA);
        vm.Library.Add(itemB);

        // Mở standalone (OpenLibraryItem)
        vm.OpenLibraryItemCommand.Execute(itemA);

        Assert.False(vm.IsWorkspaceSession);
        // Standalone không tích lũy tab qua OpenOrActivate -> Tabs.Tabs rỗng
        Assert.Empty(vm.Tabs.Tabs);

        // Mở standalone lần hai -> vẫn không tích lũy tab
        vm.OpenLibraryItemCommand.Execute(itemB);

        Assert.False(vm.IsWorkspaceSession);
        Assert.Empty(vm.Tabs.Tabs);
    }

    // =========================================================
    // Bug fix: zoom/trang của tab active phải là MỘT nguồn sự thật với toolbar.
    // Triệu chứng 1: Ctrl+scroll (viewer ghi OpenTab.Zoom) không cập nhật ZoomLevel trên toolbar.
    // =========================================================
    [Fact]
    public void ActiveTabZoom_ReflectedInViewModelZoomLevel()
    {
        var (vm, wsStore) = MakeVm();
        var W = new Workspace("ws-z", "Z", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");
        var item = MakeItem("docA", "A", "/a.pdf");
        vm.Library.Add(item);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(item);

        // Mô phỏng Ctrl+scroll: per-tab viewer ghi OpenTab.Zoom qua binding TwoWay.
        vm.Tabs.ActiveTab!.Zoom = 2.0;

        // Toolbar bind vm.ZoomLevel -> phải phản ánh zoom của tab active.
        Assert.Equal(2.0, vm.ZoomLevel);
    }

    // Triệu chứng 2: chuyển tab rồi quay lại làm mất zoom state.
    [Fact]
    public void SwitchTabAndBack_PreservesPerTabZoom()
    {
        var (vm, wsStore) = MakeVm();
        var W = new Workspace("ws-z2", "Z2", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");
        wsStore.AddDocument(W.Id, "docB");
        var a = MakeItem("docA", "A", "/a.pdf");
        var b = MakeItem("docB", "B", "/b.pdf");
        vm.Library.Add(a);
        vm.Library.Add(b);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(a); // tab A active
        vm.Tabs.ActiveTab!.Zoom = 2.0;              // scroll-zoom tab A
        vm.OpenWorkspaceDocumentCommand.Execute(b); // chuyển sang tab B
        var tabA = vm.Tabs.Tabs.First(t => t.DocumentId == "docA");
        vm.ActivateTabCommand.Execute(tabA);        // quay lại tab A

        Assert.Equal(2.0, tabA.Zoom);
        Assert.Equal(2.0, vm.ZoomLevel);
    }

    // Tương tự cho trang: tab active là một nguồn sự thật với toolbar.
    [Fact]
    public void SwitchTabAndBack_PreservesPerTabPage()
    {
        var (vm, wsStore) = MakeVm();
        var W = new Workspace("ws-p", "P", false, null, 1, 1);
        wsStore.Upsert(W);
        wsStore.AddDocument(W.Id, "docA");
        wsStore.AddDocument(W.Id, "docB");
        var a = MakeItem("docA", "A", "/a.pdf");
        var b = MakeItem("docB", "B", "/b.pdf");
        vm.Library.Add(a);
        vm.Library.Add(b);

        vm.OpenWorkspaceCommand.Execute(W);
        vm.OpenWorkspaceDocumentCommand.Execute(a);
        vm.Tabs.ActiveTab!.Page = 12;               // viewer ghi OpenTab.Page khi cuộn trang
        vm.OpenWorkspaceDocumentCommand.Execute(b);
        var tabA = vm.Tabs.Tabs.First(t => t.DocumentId == "docA");
        vm.ActivateTabCommand.Execute(tabA);

        Assert.Equal(12, tabA.Page);
        Assert.Equal(12, vm.CurrentPage);
    }

    // =========================================================
    // S2: RestoreOrSeedOpenSet — khôi phục lười khi có state đã lưu.
    // =========================================================
    [Fact]
    public void OpenWorkspace_WithSavedOpenSet_RestoresOrderAndActive_HydratesOnlyActiveTab()
    {
        var loader = new FakeDocumentLoader();
        var (vm, wsStore) = MakeVm(loader);
        var workspace = new Workspace("ws-restore", "R", false, null, 1, 1);
        wsStore.Upsert(workspace);
        wsStore.AddDocument(workspace.Id, "docA");
        wsStore.AddDocument(workspace.Id, "docB");
        vm.Library.Add(MakeItem("docA", "A", "/a.pdf"));
        vm.Library.Add(MakeItem("docB", "B", "/b.pdf"));
        wsStore.SaveOpenTabs(workspace.Id, new[]
        {
            new OpenTabState("docA", 0, false, 4, 1.0, 0, 10),
            new OpenTabState("docB", 1, true,  8, 2.5, 0, 20),
        });

        vm.OpenWorkspaceCommand.Execute(workspace);

        Assert.True(vm.IsWorkspaceSession);
        Assert.Equal(new[] { "docA", "docB" }, vm.Tabs.Tabs.Select(t => t.DocumentId), StringComparer.Ordinal);
        Assert.Equal("docB", vm.Tabs.ActiveTab!.DocumentId);
        Assert.Equal(8, vm.Tabs.ActiveTab!.Page);
        Assert.Equal(2.5, vm.Tabs.ActiveTab!.Zoom);
        Assert.Single(loader.LoadedPaths);
        Assert.False(vm.Tabs.Tabs.Single(t => t.DocumentId == "docA").IsHydrated);
        Assert.True(vm.Tabs.ActiveTab!.IsHydrated);
    }

    // =========================================================
    // S5 (#48): đổi tab cập nhật MRU; vượt ngưỡng thì evict tab inactive ít dùng nhất.
    // =========================================================
    [Fact]
    public void OpeningTabsBeyondWarmLimit_EvictsLeastRecentlyUsed_SwitchUpdatesMru()
    {
        var (vm, wsStore) = MakeVm();
        var workspace = new Workspace("ws-lru", "L", false, null, 1, 1);
        wsStore.Upsert(workspace);
        int tabCount = TabSetViewModel.DefaultMaxWarmInactiveTabs + 2;
        var items = Enumerable.Range(0, tabCount)
            .Select(i => MakeItem($"doc{i}", $"T{i}", $"/{i}.pdf"))
            .ToList();
        foreach (var item in items)
        {
            wsStore.AddDocument(workspace.Id, item.DocumentId);
            vm.Library.Add(item);
        }
        vm.OpenWorkspaceCommand.Execute(workspace);
        vm.OpenWorkspaceDocumentCommand.Execute(items[0]);

        // doc0 vừa được dùng lại nên victim phải là doc1, tab inactive ít dùng nhất.
        for (int i = 1; i < tabCount - 1; i++)
            vm.OpenWorkspaceDocumentCommand.Execute(items[i]);
        vm.ActivateTabCommand.Execute(vm.Tabs.Tabs.Single(t => t.DocumentId == "doc0"));
        vm.OpenWorkspaceDocumentCommand.Execute(items[tabCount - 1]);

        Assert.False(vm.Tabs.Tabs.Single(t => t.DocumentId == "doc1").IsHydrated);
        Assert.True(vm.Tabs.Tabs.Single(t => t.DocumentId == "doc0").IsHydrated);
        Assert.Equal(TabSetViewModel.DefaultMaxWarmInactiveTabs + 1, vm.Tabs.Tabs.Count(t => t.IsHydrated));
    }

    [Fact]
    public void OpenWorkspace_NoSavedState_SeedsSingleTabFromFirstMember()
    {
        var loader = new FakeDocumentLoader();
        var (vm, wsStore) = MakeVm(loader);
        var workspace = new Workspace("ws-seed", "Seed", false, null, 1, 1);
        wsStore.Upsert(workspace);
        wsStore.AddDocument(workspace.Id, "docOnly");
        vm.Library.Add(MakeItem("docOnly", "Only", "/only.pdf"));

        vm.OpenWorkspaceCommand.Execute(workspace);

        Assert.True(vm.IsWorkspaceSession);
        Assert.Single(vm.Tabs.Tabs);
        Assert.Equal("docOnly", vm.Tabs.ActiveTab!.DocumentId);
        Assert.Single(loader.LoadedPaths);
    }

    // =========================================================
    // S2: SaveOpenSetNow lưu tất cả tab với thứ tự, tab active và view-state.
    // =========================================================
    [Fact]
    public void SaveOpenSetNow_PersistsTabsWithOrderActiveAndViewState()
    {
        var (vm, wsStore) = MakeVm();
        var workspace = new Workspace("ws-save", "Save", false, null, 1, 1);
        wsStore.Upsert(workspace);
        wsStore.AddDocument(workspace.Id, "docA");
        wsStore.AddDocument(workspace.Id, "docB");
        vm.Library.Add(MakeItem("docA", "A", "/a.pdf"));
        vm.Library.Add(MakeItem("docB", "B", "/b.pdf"));

        vm.OpenWorkspaceCommand.Execute(workspace);
        vm.OpenWorkspaceDocumentCommand.Execute(MakeItem("docA", "A", "/a.pdf"));
        vm.OpenWorkspaceDocumentCommand.Execute(MakeItem("docB", "B", "/b.pdf")); // docB active
        vm.Tabs.ActiveTab!.Page = 9;
        vm.Tabs.ActiveTab!.Zoom = 1.5;

        vm.SaveOpenSetNow();

        var saved = wsStore.GetOpenTabs(workspace.Id);
        Assert.Equal(vm.Tabs.Tabs.Count, saved.Count);
        var activeRow = saved.Single(t => t.IsActive);
        Assert.Equal("docB", activeRow.DocumentId);
        Assert.Equal(9, activeRow.Page);
        Assert.Equal(1.5, activeRow.Zoom);
        for (int order = 0; order < saved.Count; order++)
            Assert.Equal(vm.Tabs.Tabs[order].DocumentId, saved[order].DocumentId);
    }

    // =========================================================
    // REGRESSION (S2): vào lại cùng Workspace KHÔNG được nhân đôi Open Set,
    // và cùng một tài liệu KHÔNG được mở thành nhiều tab.
    // =========================================================
    [Fact]
    public void OpenWorkspace_EnteredTwice_DoesNotDuplicateTabs()
    {
        var (vm, wsStore) = MakeVm();
        var workspace = new Workspace("ws-reenter", "Re", false, null, 1, 1);
        wsStore.Upsert(workspace);
        wsStore.AddDocument(workspace.Id, "docA");
        vm.Library.Add(MakeItem("docA", "A", "/a.pdf"));
        wsStore.SaveOpenTabs(workspace.Id, new[] { new OpenTabState("docA", 0, true, 1, 1.0, 0, 1) });

        vm.OpenWorkspaceCommand.Execute(workspace);
        int afterFirstEntry = vm.Tabs.Tabs.Count;
        vm.OpenWorkspaceCommand.Execute(workspace);

        Assert.Equal(afterFirstEntry, vm.Tabs.Tabs.Count);
        Assert.Single(vm.Tabs.Tabs, t => string.Equals(t.DocumentId, "docA", StringComparison.Ordinal));
    }

    // REGRESSION (S2): chuyển sang Workspace khác phải THAY THẾ Open Set, không tích lũy tab cũ.
    [Fact]
    public void SwitchingWorkspace_ReplacesOpenSet_DoesNotAccumulate()
    {
        var (vm, wsStore) = MakeVm();
        var workspaceA = new Workspace("ws-A", "A", false, null, 1, 1);
        var workspaceB = new Workspace("ws-B", "B", false, null, 1, 1);
        wsStore.Upsert(workspaceA);
        wsStore.Upsert(workspaceB);
        wsStore.AddDocument(workspaceA.Id, "docA");
        wsStore.AddDocument(workspaceB.Id, "docB");
        vm.Library.Add(MakeItem("docA", "A", "/a.pdf"));
        vm.Library.Add(MakeItem("docB", "B", "/b.pdf"));
        wsStore.SaveOpenTabs(workspaceA.Id, new[] { new OpenTabState("docA", 0, true, 1, 1.0, 0, 1) });
        wsStore.SaveOpenTabs(workspaceB.Id, new[] { new OpenTabState("docB", 0, true, 1, 1.0, 0, 1) });

        vm.OpenWorkspaceCommand.Execute(workspaceA);
        vm.OpenWorkspaceCommand.Execute(workspaceB);

        Assert.Single(vm.Tabs.Tabs);
        Assert.Equal("docB", vm.Tabs.ActiveTab!.DocumentId);
    }

    // =========================================================
    // #106: bấm kết quả tìm Toàn Workspace ở tài liệu khác -> đổi tab nhưng giữ ô tìm, kết quả, phạm vi
    // để người dùng bấm tiếp các kết quả khác; highlight dùng lại query cho tài liệu mới.
    // =========================================================
    [Fact]
    public void WorkspaceSearch_JumpToOtherDocument_KeepsQueryResultsAndScope()
    {
        var (vm, wsStore) = MakeVm();
        var w = new Workspace("ws-s", "S", false, null, 1, 1);
        wsStore.Upsert(w);
        wsStore.AddDocument(w.Id, "docA");
        wsStore.AddDocument(w.Id, "docB");
        var a = MakeItem("docA", "A", "/a.pdf");
        var b = MakeItem("docB", "B", "/b.pdf");
        vm.Library.Add(a);
        vm.Library.Add(b);
        vm.OpenWorkspaceCommand.Execute(w);
        vm.OpenWorkspaceDocumentCommand.Execute(a);

        vm.Search.Scope = DocumentScope.Workspace;
        vm.Search.Query = "hành";
        var hitB = new SearchResult(4, "s", 1, "docB", "B");
        var hitA = new SearchResult(1, "s", 2, "docA", "A");
        vm.Search.Results.Add(hitB);
        vm.Search.Results.Add(hitA);

        vm.Search.SelectResultCommand.Execute(hitB);

        Assert.Equal("docB", vm.Tabs.ActiveTab!.DocumentId);
        Assert.Equal(5, vm.CurrentPage);
        Assert.Equal("hành", vm.Search.Query);
        Assert.Equal("hành", vm.Search.SelectedQuery);
        Assert.Equal(DocumentScope.Workspace, vm.Search.Scope);
        Assert.Equal([hitB, hitA], vm.Search.Results);
    }

    [Fact]
    public void ManualTabSwitch_InCurrentDocumentScope_StillResetsSearch()
    {
        var (vm, wsStore) = MakeVm();
        var w = new Workspace("ws-m", "M", false, null, 1, 1);
        wsStore.Upsert(w);
        wsStore.AddDocument(w.Id, "docA");
        wsStore.AddDocument(w.Id, "docB");
        var a = MakeItem("docA", "A", "/a.pdf");
        var b = MakeItem("docB", "B", "/b.pdf");
        vm.Library.Add(a);
        vm.Library.Add(b);
        vm.OpenWorkspaceCommand.Execute(w);
        vm.OpenWorkspaceDocumentCommand.Execute(a);
        vm.Search.Query = "hành";
        vm.Search.Results.Add(new SearchResult(0, "s", 1));

        vm.OpenWorkspaceDocumentCommand.Execute(b);

        Assert.Equal(string.Empty, vm.Search.Query);
        Assert.Empty(vm.Search.Results);
    }
}
