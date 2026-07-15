using System.Linq;
using PdfReaderApp.Models;
using PdfReaderApp.Services;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

/// <summary>#47: surface Tài liệu Workspace qua seam MainViewModel (AC test seam).</summary>
public class MainViewModelDocumentsSurfaceTests
{
    private sealed class TestableMainViewModel : MainViewModel
    {
        public TestableMainViewModel(IWorkspaceStore wsStore)
            : base(
                new ITextPdfDocumentService(),
                new WindowsSettingsService(),
                new OpenAiChatClientFactory(),
                new SqliteDocumentIndex(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.IO.Path.GetRandomFileName() + ".db"),
                    System.IO.Path.Combine(System.AppContext.BaseDirectory, "vec0.dll")),
                new OpenAiEmbeddingGeneratorFactory(),
                workspaceStore: wsStore)
        { }

        protected override void HydrateTab(OpenTab tab) { /* không nạp PDF thật */ }
    }

    private static (TestableMainViewModel vm, FakeWorkspaceStore wsStore, Workspace ws) MakeVmInWorkspace()
    {
        var wsStore = new FakeWorkspaceStore();
        var ws = new Workspace("ws1", "Nghiên cứu", false, null, 1, 1);
        wsStore.Upsert(ws);
        var vm = new TestableMainViewModel(wsStore);
        vm.SelectedWorkspace = ws;
        return (vm, wsStore, ws);
    }

    private static LibraryItem Seed(TestableMainViewModel vm, string docId, string title)
    {
        var item = new LibraryItem(docId, title, $"/lib/{docId}.pdf", null, 5, 1, 1);
        vm.Library.Add(item);
        return item;
    }

    [Fact]
    public void PlusCommand_RefreshesZones_AndOpensModal()
    {
        var (vm, wsStore, _) = MakeVmInWorkspace();
        Seed(vm, "docA", "A");
        Seed(vm, "docB", "B");
        wsStore.AddDocument("ws1", "docA");

        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        Assert.True(vm.DocumentsSurface.IsModalOpen);
        Assert.Single(vm.DocumentsSurface.Members, m => m.DocumentId == "docA");
        Assert.Single(vm.DocumentsSurface.LibraryAdditions, l => l.DocumentId == "docB");
    }

    [Fact]
    public void AddFromLibrary_AddsMembership_OpensActiveTab_PersistsOpenSet_ClosesModal()
    {
        var (vm, wsStore, _) = MakeVmInWorkspace();
        var item = Seed(vm, "docB", "B");
        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        vm.DocumentsSurface.AddFromLibraryCommand.Execute(item);

        Assert.Contains("docB", wsStore.GetDocumentIds("ws1"));
        Assert.Equal("docB", vm.Tabs.ActiveTab!.DocumentId);
        Assert.Contains(wsStore.OpenSets["ws1"], t => t.DocumentId == "docB" && t.IsActive);
        Assert.False(vm.DocumentsSurface.IsModalOpen);
    }

    [Fact]
    public void RemoveMember_WhoseTabIsOpen_RemovesMembership_AndClosesTab()
    {
        var (vm, wsStore, _) = MakeVmInWorkspace();
        var itemA = Seed(vm, "docA", "A");
        var itemB = Seed(vm, "docB", "B");
        wsStore.AddDocument("ws1", "docA");
        wsStore.AddDocument("ws1", "docB");
        vm.OpenWorkspaceDocumentCommand.Execute(itemA);
        vm.OpenWorkspaceDocumentCommand.Execute(itemB);   // active = docB
        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        vm.DocumentsSurface.RemoveMemberCommand.Execute(itemB);

        Assert.DoesNotContain("docB", wsStore.GetDocumentIds("ws1"));
        Assert.DoesNotContain(vm.Tabs.Tabs, t => t.DocumentId == "docB");
        Assert.Equal("docA", vm.Tabs.ActiveTab!.DocumentId);   // MRU promote
    }

    [Fact]
    public void RemoveMember_LastOpenTab_LeavesEmptyOpenSet_StillWorkspaceSession()
    {
        var (vm, wsStore, _) = MakeVmInWorkspace();
        var item = Seed(vm, "docA", "A");
        wsStore.AddDocument("ws1", "docA");
        vm.OpenWorkspaceDocumentCommand.Execute(item);

        vm.DocumentsSurface.RemoveMemberCommand.Execute(item);

        Assert.False(vm.Tabs.HasTabs);
        Assert.True(vm.IsWorkspaceSession);   // canvas hiện surface inline, không rơi về màn nào khác
    }

    [Fact]
    public void SelectMember_ActivatesExistingTab_NoDuplicate()
    {
        var (vm, wsStore, _) = MakeVmInWorkspace();
        var item = Seed(vm, "docA", "A");
        wsStore.AddDocument("ws1", "docA");
        vm.OpenWorkspaceDocumentCommand.Execute(item);
        vm.ShowWorkspaceDocumentsCommand.Execute(null);

        vm.DocumentsSurface.OpenMemberCommand.Execute(item);

        Assert.Single(vm.Tabs.Tabs);
        Assert.Equal("docA", vm.Tabs.ActiveTab!.DocumentId);
        Assert.False(vm.DocumentsSurface.IsModalOpen);
    }

    [Fact]
    public void OpenWorkspace_Empty_EntersSessionWithEmptyOpenSet()
    {
        var (vm, _, ws) = MakeVmInWorkspace();

        vm.OpenWorkspaceCommand.Execute(ws);

        Assert.True(vm.IsWorkspaceSession);
        Assert.False(vm.Tabs.HasTabs);
        Assert.False(vm.ShowWorkspaces);
    }

    [Fact]
    public void SurfaceRename_RefreshesSelectedWorkspace_AndCards()
    {
        var (vm, wsStore, _) = MakeVmInWorkspace();
        vm.ShowWorkspaceDocumentsCommand.Execute(null);
        vm.DocumentsSurface.BeginRenameCommand.Execute(null);
        vm.DocumentsSurface.RenameDraft = "Đề tài mới";

        vm.DocumentsSurface.CommitRenameCommand.Execute(null);

        Assert.Equal("Đề tài mới", wsStore.Get("ws1")!.Name);
        Assert.Equal("Đề tài mới", vm.SelectedWorkspace!.Name);
        Assert.Equal("Đề tài mới", vm.DocumentsSurface.WorkspaceName);
    }

    // #88: RemoveLibraryItem trên doc đang có Tab mở trong workspace active phải đóng Tab đó.
    [Fact]
    public void RemoveLibraryItem_WhenDocInActiveWorkspace_WithOpenTab_ClosesTab()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = new TestableMainViewModel(wsStore);
        var ws = new Workspace("ws1", "Dự án", false, null, 1, 1);
        wsStore.Upsert(ws);
        var item = new LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 5, 1, 1);
        vm.Library.Add(item);
        wsStore.AddDocument("ws1", "docA");
        vm.OpenWorkspaceCommand.Execute(ws);   // seed docA tab, _activeWorkspaceId = "ws1"
        Assert.True(vm.Tabs.HasTabs, "precondition: tab docA phai ton tai truoc khi xoa");

        vm.RemoveLibraryItemCommand.Execute(item);

        Assert.DoesNotContain(vm.Tabs.Tabs, t => t.DocumentId == "docA");
    }

    // #88 edge case: không crash khi doc không có Tab đang mở.
    [Fact]
    public void RemoveLibraryItem_WhenDocInActiveWorkspace_NoOpenTab_DoesNotCrash()
    {
        var wsStore = new FakeWorkspaceStore();
        var vm = new TestableMainViewModel(wsStore);
        var ws = new Workspace("ws1", "Dự án", false, null, 1, 1);
        wsStore.Upsert(ws);
        var item = new LibraryItem("docA", "Sách A", "/lib/a.pdf", null, 5, 1, 1);
        vm.Library.Add(item);
        wsStore.AddDocument("ws1", "docA");
        vm.OpenWorkspaceCommand.Execute(ws);
        vm.CloseTabCommand.Execute(vm.Tabs.ActiveTab);   // đóng tab trước
        Assert.False(vm.Tabs.HasTabs, "precondition: khong co tab");

        var ex = Record.Exception(() => vm.RemoveLibraryItemCommand.Execute(item));

        Assert.Null(ex);
        Assert.False(vm.Tabs.HasTabs);
    }

    // Reproduce test: DeleteWorkspace khi active phải không để lại library items
    // trong LibraryAdditions (stale Refresh sau Tabs.Reset() với SelectedWorkspace chưa null).
    [Fact]
    public void DeleteWorkspace_WhenActive_LibraryNotInflatedIntoSurface()
    {
        var (vm, wsStore, ws) = MakeVmInWorkspace();
        var doc = Seed(vm, "docA", "A");
        wsStore.AddDocument("ws1", "docA");
        vm.OpenWorkspaceDocumentCommand.Execute(doc);

        vm.DeleteWorkspaceCommand.Execute(ws);

        Assert.Empty(vm.DocumentsSurface.LibraryAdditions);
    }

    // Reproduce test cho bug #2: DeleteWorkspace trên workspace đang active
    // phải đóng hết tab, thoát session, và tắt modal.
    [Fact]
    public void DeleteWorkspace_WhenActive_WithOpenTabsAndModal_ClosesTabsExitsSessionClearsModal()
    {
        var (vm, wsStore, ws) = MakeVmInWorkspace();
        var doc = Seed(vm, "docA", "A");
        wsStore.AddDocument("ws1", "docA");
        vm.OpenWorkspaceDocumentCommand.Execute(doc);     // enters session, opens tab
        vm.ShowWorkspaceDocumentsCommand.Execute(null);   // opens modal
        Assert.True(vm.Tabs.HasTabs);
        Assert.True(vm.IsWorkspaceSession);
        Assert.True(vm.DocumentsSurface.IsModalOpen);

        vm.DeleteWorkspaceCommand.Execute(ws);

        Assert.False(vm.Tabs.HasTabs, "tabs phai dong het");
        Assert.False(vm.IsWorkspaceSession, "phai thoat workspace session");
        Assert.False(vm.DocumentsSurface.IsModalOpen, "modal phai tat");
    }
}
