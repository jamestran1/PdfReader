using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

/// <summary>
/// TDD cho ActivateTabCommand và CloseTabCommand trên MainViewModel (Tab S1 UI layer).
/// Dùng TestableMainViewModel (override HydrateTab = no-op) -- kế thừa pattern từ MainViewModelTabTests.
/// </summary>
public class MainViewModelTabCommandTests
{
    // --- Test seam: no-op HydrateTab (giống MainViewModelTabTests) ---

    private sealed class TestableMainViewModel : MainViewModel
    {
        public TestableMainViewModel(IWorkspaceStore wsStore)
            : base(
                new ITextPdfDocumentService(),
                new WindowsSettingsService(),
                new OpenAiChatClientFactory(),
                new PdfReaderApp.Services.SqliteDocumentIndex(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db"),
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
                new OpenAiEmbeddingGeneratorFactory(),
                workspaceStore: wsStore)
        { }

        protected override void HydrateTab(OpenTab tab) { /* no-op */ }
    }

    private sealed class FakeWorkspaceStore : IWorkspaceStore
    {
        public readonly List<Workspace> All = new();
        public readonly Dictionary<string, HashSet<string>> Membership = new();
        public int RemoveDocumentCallCount { get; private set; }

        public void EnsureSchema() { }
        public void Upsert(Workspace w) { All.RemoveAll(x => x.Id == w.Id); All.Add(w); }
        public Workspace? Get(string id) => All.FirstOrDefault(w => w.Id == id);
        public IReadOnlyList<Workspace> GetAll(bool includeDefault)
            => All.Where(w => includeDefault || !w.IsDefault).ToList();
        public void AddDocument(string workspaceId, string documentId)
        {
            if (!Membership.TryGetValue(workspaceId, out var s)) { s = new(); Membership[workspaceId] = s; }
            s.Add(documentId);
        }
        public void RemoveDocument(string workspaceId, string documentId)
        {
            RemoveDocumentCallCount++;
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
        public readonly Dictionary<string, List<OpenTabState>> OpenSets = new();

        public void SaveOpenTabs(string workspaceId, IReadOnlyList<OpenTabState> tabs)
            => OpenSets[workspaceId] = tabs.OrderBy(t => t.TabOrder).ToList();

        public IReadOnlyList<OpenTabState> GetOpenTabs(string workspaceId)
            => OpenSets.TryGetValue(workspaceId, out var s) ? s.ToList() : new List<OpenTabState>();
    }

    private static (TestableMainViewModel vm, FakeWorkspaceStore wsStore) MakeVm()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = new TestableMainViewModel(wsStore);
        return (vm, wsStore);
    }

    private static LibraryItem MakeItem(string docId, string title = "Tài liệu", string path = "/path/doc.pdf")
        => new LibraryItem(docId, title, path, null, 0, 1, 1);

    // Mở workspace và hai tài liệu -> trả về cả hai tab để test.
    private static (OpenTab tabA, OpenTab tabB) OpenTwoTabs(TestableMainViewModel vm, FakeWorkspaceStore wsStore)
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

        return (vm.Tabs.Tabs[0], vm.Tabs.Tabs[1]);
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

        Assert.Equal(tabA, vm.Tabs.ActiveTab);
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
        Assert.Contains("docA", wsStore.GetDocumentIds("ws-cmd"));
        Assert.Contains("docB", wsStore.GetDocumentIds("ws-cmd"));
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

        Assert.Single(vm.Tabs.Tabs);
        Assert.DoesNotContain(tabB, vm.Tabs.Tabs);
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
        Assert.Contains("docB", wsStore.GetDocumentIds("ws-cmd"));
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

        Assert.Equal(tabA, vm.Tabs.ActiveTab);
    }
}
