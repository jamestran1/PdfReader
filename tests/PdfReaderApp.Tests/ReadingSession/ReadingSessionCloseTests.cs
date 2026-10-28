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
        Assert.Contains($"load:{ReadingSessionFixture.PathOf("B")}", f.Log.Entries);
    }

    [Fact]
    public void Close_InactiveTab_KeepsActiveTabAndLoadsNothing()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Close(tabA);

        Assert.Equal("B", f.Session.Tabs.ActiveTab?.DocumentId);
        Assert.DoesNotContain(f.Log.Entries, e => e.StartsWith("load:", StringComparison.Ordinal));
    }

    [Fact]
    public void Close_LastTab_RefreshesSurfaceAndLoadsNoDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");

        f.Session.Close(tabA);

        Assert.Contains("surface.refresh", f.Log.Entries);
        Assert.DoesNotContain(f.Log.Entries, e => e.StartsWith("load:", StringComparison.Ordinal));
        Assert.False(f.Session.Tabs.HasTabs);
    }

    [Fact]
    public void Close_LastTab_CancelsPendingChat()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");

        f.Session.Close(tabA);

        Assert.Contains("chat.cancel", f.Log.Entries);
    }

    [Fact]
    public void Close_Tab_PersistsOpenSetWithoutTheClosedTab()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        var tabB = f.OpenTab("B");

        f.Session.Close(tabB);

        var saved = f.Store.GetOpenTabs(ReadingSessionFixture.WorkspaceId);
        Assert.Equal(["A"], saved.Select(t => t.DocumentId));
    }
}
