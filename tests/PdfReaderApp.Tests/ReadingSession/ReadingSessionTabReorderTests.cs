using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>Kéo-thả sắp lại Tab (#262): thứ tự mới được lưu vào tab_order và khôi phục khi vào lại Workspace, ở cả hai đường host.</summary>
public class ReadingSessionTabReorderTests
{
    private const string WorkspaceId = ReadingSessionFixture.WorkspaceId;

    private static Composition Make(string id, string workspaceId)
        => new(id, workspaceId, $"Bản {id}", CompositionKind.Summary, "# nội dung", "m", [], 1);

    private static string Order(ReadingSessionFixture f) => string.Join(",", f.Session.CurrentTabs.Tabs.Select(t => t.ItemId));

    private static string SavedOrder(ReadingSessionFixture f, string workspaceId)
        => string.Join(",", f.Store.GetOpenTabs(workspaceId).OrderBy(t => t.TabOrder).Select(t => t.ItemId));

    // Workspace có tên: Tab Document A, B và Tab Bản soạn X xen kẽ, X đang active.
    private static (ReadingSessionFixture F, OpenTab A, OpenTab B, CompositionTab X) NamedWorkspaceWithMixedTabs()
    {
        var f = new ReadingSessionFixture().InLibrary("A").InLibrary("B").InWorkspace();
        var a = f.OpenTab("A");
        var b = f.OpenTab("B");
        var composition = Make("X", WorkspaceId);
        f.Compositions["X"] = composition;
        var x = f.Session.OpenComposition(composition);
        return (f, a, b, x);
    }

    [Fact]
    public void Move_InNamedWorkspace_SavesTheNewTabOrderAndKeepsTheActiveTab()
    {
        var (f, _, _, x) = NamedWorkspaceWithMixedTabs();

        f.Session.Move(x, 0);

        Assert.Equal("X,A,B", Order(f));
        Assert.Equal("X,A,B", SavedOrder(f, WorkspaceId));
        Assert.Equal([0, 1, 2], f.Store.GetOpenTabs(WorkspaceId).Select(t => t.TabOrder));
        Assert.Same(x, f.Session.Tabs.ActiveTab);
        Assert.Equal("X", f.Store.GetOpenTabs(WorkspaceId).Single(t => t.IsActive).ItemId);
    }

    [Fact]
    public void ReenteringANamedWorkspace_RestoresTheReorderedTabsInterleaved()
    {
        var (f, a, _, _) = NamedWorkspaceWithMixedTabs();
        f.Session.Move(a, 2);   // B,X,A

        f.Session.RestoreOpenSet(WorkspaceId);

        Assert.Equal("B,X,A", Order(f));
    }

    [Fact]
    public void MoveToSlot_ConvertsTheDropGapToTheFinalIndexAndSaves()
    {
        var (f, a, _, _) = NamedWorkspaceWithMixedTabs();   // A,B,X

        bool moved = f.Session.MoveToSlot(a, 3);   // thả sau tab cuối

        Assert.True(moved);
        Assert.Equal("B,X,A", Order(f));
        Assert.Equal("B,X,A", SavedOrder(f, WorkspaceId));
    }

    [Theory]
    [InlineData(0)]   // khe ngay trước chính nó
    [InlineData(1)]   // khe ngay sau chính nó
    public void MoveToSlot_BesideTheDraggedTab_IsANoOpAndDoesNotSave(int slot)
    {
        var (f, a, _, _) = NamedWorkspaceWithMixedTabs();
        f.Store.OpenSets.Clear();

        bool moved = f.Session.MoveToSlot(a, slot);

        Assert.False(moved);
        Assert.True(f.Session.IsNoOpDrop(a, slot));
        Assert.Equal("A,B,X", Order(f));
        Assert.Empty(f.Store.OpenSets);
    }

    [Fact]
    public void LightStrip_DocumentTabIsPinned_AndDropsBeforeItLandRightAfterIt()
    {
        var f = new ReadingSessionFixture().InLibrary("A");
        f.Session.Open(ReadingSessionFixture.PathOf("A"));
        string workspaceId = f.Store.All.Single(w => w.IsDefault && w.DefaultDocumentId == "A").Id;
        foreach (string id in new[] { "X", "Y" })
        {
            f.Compositions[id] = Make(id, workspaceId);
            f.Session.OpenComposition(f.Compositions[id]);
        }

        var document = f.Session.CurrentTabs.Tabs[0];
        var y = f.Session.FindCompositionTab("Y")!;

        Assert.False(f.Session.CanDrag(document));
        Assert.Equal(1, f.Session.FirstDropSlot);
        Assert.False(f.Session.MoveToSlot(document, 2));

        Assert.True(f.Session.MoveToSlot(y, 0));   // khe 0 bị kẹp về khe 1
        Assert.Equal("A,Y,X", Order(f));
        Assert.True(f.Session.IsNoOpDrop(y, 0));
    }

    [Fact]
    public void Move_InDefaultWorkspace_SavesTheCompositionTabsInTheirNewOrder()
    {
        var f = new ReadingSessionFixture().InLibrary("A").InLibrary("B");
        f.Session.Open(ReadingSessionFixture.PathOf("A"));
        string workspaceId = f.Store.All.Single(w => w.IsDefault && w.DefaultDocumentId == "A").Id;
        foreach (string id in new[] { "X", "Y", "Z" })
        {
            f.Compositions[id] = Make(id, workspaceId);
            f.Session.OpenComposition(f.Compositions[id]);
        }

        f.Session.Move(f.Session.FindCompositionTab("Z")!, 1);   // A,Z,X,Y

        Assert.Equal("A,Z,X,Y", Order(f));
        Assert.Equal("Z,X,Y", SavedOrder(f, workspaceId));   // Tab Document không ghi (ADR 0009), chỉ thứ tự giữa các Tab Bản soạn
        Assert.Equal([0, 1, 2], f.Store.GetOpenTabs(workspaceId).Select(t => t.TabOrder));

        f.Session.Open(ReadingSessionFixture.PathOf("B"));
        f.Session.Open(ReadingSessionFixture.PathOf("A"));

        Assert.Equal("A,Z,X,Y", Order(f));
    }
}
