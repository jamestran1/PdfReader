using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

/// <summary>
/// TDD vertical slices cho phần wiring Tab S1 trong MainViewModel (hành vi 8-10).
/// Dùng TestableMainViewModel (override HydrateTab = no-op) để không nạp PDF thật.
/// </summary>
public class MainViewModelTabTests
{
    // --- Test seam: no-op HydrateTab ---

    private sealed class TestableMainViewModel : MainViewModel
    {
        public int HydrateCallCount { get; private set; }
        public List<OpenTab> HydratedTabs { get; } = new();

        public TestableMainViewModel(
            IWorkspaceStore wsStore,
            INoteStore? noteStore = null)
            : base(
                new ITextPdfDocumentService(),
                new WindowsSettingsService(),
                new OpenAiChatClientFactory(),
                new PdfReaderApp.Services.SqliteDocumentIndex(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db"),
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
                new OpenAiEmbeddingGeneratorFactory(),
                workspaceStore: wsStore,
                noteStore: noteStore)
        { }

        protected override void HydrateTab(OpenTab tab)
        {
            // No-op: không nạp PDF thật.
            HydrateCallCount++;
            HydratedTabs.Add(tab);
        }
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
    }

    private static (TestableMainViewModel vm, FakeWorkspaceStore wsStore) MakeVm()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = new TestableMainViewModel(wsStore);
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
        Assert.Contains("docA", wsStore.GetDocumentIds(W.Id));
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
        Assert.Contains("docA", wsStore.GetDocumentIds(W.Id));
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
}
