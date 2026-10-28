using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionOpenInWorkspaceTests
{
    private static LibraryItem Item(string documentId)
        => new(documentId, documentId, ReadingSessionFixture.PathOf(documentId), null, 1, 0, 0);

    [Fact]
    public void OpenInWorkspace_MemberWithoutTab_OpensActivatesLoadsAndRecordsOpenedTime()
    {
        var f = new ReadingSessionFixture();
        f.Loader.Returns(ReadingSessionFixture.PathOf("M"), new LoadedDocument("M", [], []));

        f.Session.OpenInWorkspace(ReadingSessionFixture.WorkspaceId, Item("M"));

        Assert.True(f.Session.IsWorkspaceSession);
        Assert.Equal(ReadingSessionFixture.WorkspaceId, f.Session.ActiveWorkspaceId);
        Assert.Equal("M", Assert.Single(f.Session.Tabs.Tabs).DocumentId);
        Assert.Equal("M", f.Session.Tabs.ActiveTab?.DocumentId);
        Assert.Equal("M", f.Session.DocumentId);
        Assert.Equal(["M"], f.Opened);
    }

    [Fact]
    public void OpenInWorkspace_MemberAlreadyOpen_ActivatesExistingTabWithoutDuplicate()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabM = f.OpenTab("M");
        f.OpenTab("N");

        f.Session.OpenInWorkspace(ReadingSessionFixture.WorkspaceId, Item("M"));

        Assert.Equal(2, f.Session.Tabs.Tabs.Count);
        Assert.Same(tabM, f.Session.Tabs.ActiveTab);
        Assert.Equal("M", f.Session.DocumentId);
    }

    [Fact]
    public void OpenInWorkspace_WhenRecordingOpenedTimeFails_StillOpensTab()
    {
        var f = new ReadingSessionFixture { FailMarkOpened = true };

        f.Session.OpenInWorkspace(ReadingSessionFixture.WorkspaceId, Item("M"));

        Assert.Equal("M", f.Session.Tabs.ActiveTab?.DocumentId);
        Assert.Empty(f.Errors);
    }
}
