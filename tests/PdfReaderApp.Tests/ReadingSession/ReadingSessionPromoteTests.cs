using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionPromoteTests
{
    private static Workspace NewWorkspace(string name)
        => new("ws-luan-van", name, false, null, 1, 1);

    private static ReadingSessionFixture Reading(string documentId)
    {
        var f = new ReadingSessionFixture();
        f.Loader.Returns(ReadingSessionFixture.PathOf(documentId), new LoadedDocument(documentId, [], []));
        f.Session.Open(ReadingSessionFixture.PathOf(documentId));
        return f;
    }

    [Fact]
    public void Promote_CarriesPageAndZoomIntoTheOnlyActiveTab()
    {
        var f = Reading("D");
        f.Session.CurrentPage = 12;
        f.Session.ZoomLevel = 1.5;

        bool promoted = f.Session.Promote(NewWorkspace("Luận văn"), "Tài liệu D");

        Assert.True(promoted);
        var tab = Assert.Single(f.Session.Tabs.Tabs);
        Assert.Same(tab, f.Session.Tabs.ActiveTab);
        Assert.Equal("D", tab.DocumentId);
        Assert.Equal("Tài liệu D", tab.Title);
        Assert.Equal(12, tab.Page);
        Assert.Equal(1.5, tab.Zoom);
        Assert.Equal(12, f.Session.CurrentPage);
        Assert.Equal(1.5, f.Session.ZoomLevel);
    }

    [Fact]
    public void Promote_MakesDocumentAMemberAndSwitchesToTheNewWorkspace()
    {
        var f = Reading("D");

        f.Session.Promote(NewWorkspace("Luận văn"), "Tài liệu D");

        Assert.True(f.Session.IsWorkspaceSession);
        Assert.Equal("ws-luan-van", f.Session.ActiveWorkspaceId);
        Assert.Equal("Luận văn", f.Store.Get("ws-luan-van")?.Name);
        Assert.Contains("D", f.Store.GetDocumentIds("ws-luan-van"), StringComparer.Ordinal);
    }

    [Fact]
    public void Promote_WhenNoDocumentIsOpen_DoesNothing()
    {
        var f = new ReadingSessionFixture();

        bool promoted = f.Session.Promote(NewWorkspace("Luận văn"), "x");

        Assert.False(promoted);
        Assert.Empty(f.Store.All);
        Assert.False(f.Session.IsWorkspaceSession);
    }

    [Fact]
    public void Promote_WhenAlreadyInNamedWorkspace_DoesNothing()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");

        bool promoted = f.Session.Promote(NewWorkspace("Khác"), "A");

        Assert.False(promoted);
        Assert.Empty(f.Store.All);
        Assert.Single(f.Session.Tabs.Tabs);
    }
}
