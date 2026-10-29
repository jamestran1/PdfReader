namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionCloseTests
{
    [Fact]
    public void Close_ActiveTab_ActivatesMostRecentlyUsedTabAndLoadsIt()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        f.OpenTab("B");
        var tabC = f.OpenTab("C");

        f.Session.Close(tabC);

        Assert.Equal("B", f.Session.Tabs.ActiveTab?.DocumentId);
        Assert.Equal("B", f.Session.DocumentId);
        Assert.True(f.Log.Has($"load:{ReadingSessionFixture.PathOf("B")}"));
    }

    [Fact]
    public void Close_InactiveTab_KeepsActiveTabAndLoadsNothing()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Close(tabA);

        Assert.Equal("B", f.Session.Tabs.ActiveTab?.DocumentId);
        Assert.False(f.Log.HasLoad());
    }

    [Fact]
    public void Close_LastTab_RefreshesSurfaceAndLoadsNoDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");

        f.Session.Close(tabA);

        Assert.True(f.Log.Has("surface.refresh"));
        Assert.False(f.Log.HasLoad());
        Assert.False(f.Session.Tabs.HasTabs);
    }

    [Fact]
    public void Close_LastTab_ClearsDocumentState()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        Assert.True(f.Session.HasDocument);
        Assert.Equal("A", f.Session.DocumentId);

        f.Session.Close(tabA);

        Assert.False(f.Session.HasDocument);
        Assert.Null(f.Session.DocumentId);
        Assert.Null(f.Session.FilePath);
        Assert.Empty(f.Session.DocumentBlocks);
    }

    [Fact]
    public void Close_LastTab_CancelsPendingChat()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");

        f.Session.Close(tabA);

        Assert.True(f.Log.Has("chat.cancel"));
    }

    [Fact]
    public void Close_Tab_PersistsOpenSetWithoutTheClosedTab()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        var tabB = f.OpenTab("B");

        f.Session.Close(tabB);

        var saved = f.Store.GetOpenTabs(ReadingSessionFixture.WorkspaceId);
        Assert.Equal("A", Assert.Single(saved).DocumentId);
    }

    [Fact]
    public void CloseDocument_DocumentWithOpenTab_ClosesThatTab()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        f.OpenTab("B");

        f.Session.CloseDocument("A");

        Assert.Equal("B", Assert.Single(f.Session.Tabs.Tabs).DocumentId);
    }

    [Fact]
    public void CloseDocument_DocumentWithoutOpenTab_ChangesNothing()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");

        f.Session.CloseDocument("Z");

        Assert.Equal("A", Assert.Single(f.Session.Tabs.Tabs).DocumentId);
        Assert.False(f.Log.HasLoad());
    }
}
