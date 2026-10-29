using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionRestoreOpenSetTests
{
    private const string Target = "ws-target";

    private static void Save(ReadingSessionFixture f, string workspaceId, params OpenTabState[] tabs)
        => f.Store.OpenSets[workspaceId] = [.. tabs];

    private static OpenTabState Saved(string documentId, int order, bool active = false, int page = 1, double zoom = 1.0, double scroll = 0)
        => new(documentId, order, active, page, zoom, scroll, 0);

    [Fact]
    public void RestoreOpenSet_LeavingWorkspace_SavesItsOpenSetBeforeBuildingTheNewOne()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.InLibrary("A");
        f.InLibrary("B");
        f.OpenTab("A");
        Save(f, Target, Saved("B", 0, active: true));

        f.Session.RestoreOpenSet(Target);

        Assert.Equal(ReadingSessionFixture.WorkspaceId, f.Store.SavedWorkspaceIds[0]);
        Assert.Equal("A", Assert.Single(f.Store.OpenSets[ReadingSessionFixture.WorkspaceId]).DocumentId);
        Assert.Equal("B", Assert.Single(f.Session.Tabs.Tabs).DocumentId);
    }

    [Fact]
    public void RestoreOpenSet_SavedThreeTabs_RestoresOrderAndViewStateAndLoadsOnlyActiveOnce()
    {
        var f = new ReadingSessionFixture();
        foreach (var id in new[] { "A", "B", "C" }) f.InLibrary(id);
        Save(f, Target,
            Saved("A", 0, page: 3, zoom: 1.5, scroll: 0.25),
            Saved("B", 1, active: true, page: 7, zoom: 2.0),
            Saved("C", 2, page: 2));

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A,B,C", string.Join(",", f.Session.Tabs.Tabs.Select(t => t.DocumentId)));
        var first = f.Session.Tabs.Tabs[0];
        Assert.Equal((3, 1.5, 0.25), (first.Page, first.Zoom, first.ScrollNorm));
        Assert.Equal("B", f.Session.Tabs.ActiveTab?.DocumentId);
        Assert.Equal([ReadingSessionFixture.PathOf("B")], f.Loader.LoadedPaths);
        Assert.Equal(7, f.Session.CurrentPage);
        Assert.Equal([false, true, false], f.Session.Tabs.Tabs.Select(t => t.IsHydrated));   // chỉ Tab active nạp
        Assert.True(f.Session.IsWorkspaceSession);
        Assert.Equal(Target, f.Session.ActiveWorkspaceId);
    }

    [Fact]
    public void RestoreOpenSet_DoesNotWriteBuildInProgressStateAndSavesOnceWhenDone()
    {
        var f = new ReadingSessionFixture();
        foreach (var id in new[] { "A", "B" }) f.InLibrary(id);
        Save(f, Target, Saved("A", 0, page: 3), Saved("B", 1, active: true, page: 5));

        f.Session.RestoreOpenSet(Target);

        Assert.Equal([Target], f.Store.SavedWorkspaceIds);
        Assert.Equal("A,B", string.Join(",", f.Store.OpenSets[Target].Select(t => t.DocumentId)));
        Assert.Equal(3, f.Store.OpenSets[Target][0].Page);
    }

    [Fact]
    public void RestoreOpenSet_SkipsDocumentsRemovedFromLibrary()
    {
        var f = new ReadingSessionFixture();
        f.InLibrary("A");
        f.InLibrary("C");
        Save(f, Target, Saved("A", 0), Saved("GONE", 1, active: true), Saved("C", 2));

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A,C", string.Join(",", f.Session.Tabs.Tabs.Select(t => t.DocumentId)));
        Assert.Equal("A", f.Session.Tabs.ActiveTab?.DocumentId);
    }

    [Fact]
    public void RestoreOpenSet_NoSavedState_SeedsDefaultDocumentAndLoadsIt()
    {
        var f = new ReadingSessionFixture();
        f.InLibrary("A");
        f.InLibrary("B");
        f.Store.Upsert(new Workspace(Target, "Y", false, "B", 0, 0));
        f.Store.AddDocument(Target, "A");
        f.Store.AddDocument(Target, "B");

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("B", Assert.Single(f.Session.Tabs.Tabs).DocumentId);
        Assert.Equal([ReadingSessionFixture.PathOf("B")], f.Loader.LoadedPaths);
        Assert.Equal("B", Assert.Single(f.Store.OpenSets[Target]).DocumentId);
    }

    [Fact]
    public void RestoreOpenSet_NoSavedStateNoDefault_SeedsFirstMember()
    {
        var f = new ReadingSessionFixture();
        f.InLibrary("A");
        f.InLibrary("B");
        f.Store.Upsert(new Workspace(Target, "Y", false, null, 0, 0));
        f.Store.AddDocument(Target, "A");
        f.Store.AddDocument(Target, "B");

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A", Assert.Single(f.Session.Tabs.Tabs).DocumentId);
    }

    [Fact]
    public void RestoreOpenSet_EmptyWorkspace_LeavesEmptyOpenSetInWorkspaceSessionWithoutLoading()
    {
        var f = new ReadingSessionFixture();
        f.Store.Upsert(new Workspace(Target, "Y", false, null, 0, 0));

        f.Session.RestoreOpenSet(Target);

        Assert.False(f.Session.Tabs.HasTabs);
        Assert.True(f.Session.IsWorkspaceSession);
        Assert.Equal(Target, f.Session.ActiveWorkspaceId);
        Assert.False(f.Log.HasLoad());
        Assert.Equal([Target], f.Store.SavedWorkspaceIds);
    }
}
