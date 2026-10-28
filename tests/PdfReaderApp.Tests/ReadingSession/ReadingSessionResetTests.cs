namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionResetTests
{
    [Fact]
    public void Reset_AfterWorkspaceOpened_LeavesNoWorkspaceNoTabsNoDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Reset();

        Assert.Empty(f.Session.Tabs.Tabs);
        Assert.Null(f.Session.ActiveWorkspaceId);
        Assert.False(f.Session.IsWorkspaceSession);
    }

    [Fact]
    public void Reset_ClearsNotesClosesSurfaceModalAndLoadsNoDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");

        f.Session.Reset();

        Assert.True(f.Log.Has("notes.load:null"));
        Assert.True(f.Log.Has("surface.dismissModal"));
        Assert.False(f.Log.HasLoad());
    }

    [Fact]
    public void Reset_DoesNotRewriteOpenSetOfTheResetWorkspace()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");

        f.Session.Reset();

        Assert.Equal("A", Assert.Single(f.Store.GetOpenTabs(ReadingSessionFixture.WorkspaceId)).DocumentId);
    }
}
