using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>Default Workspace (đọc lẻ) có Tab Strip nhẹ: Tab Document trỏ viewer standalone cộng Tab Bản soạn (ADR 0009).</summary>
public class ReadingSessionDefaultWorkspaceTabsTests
{
    private static Composition Make(string id, string owner)
        => new(id, owner, $"Bản {id}", CompositionKind.Summary, "# nội dung", "m", [], 1);

    private static ReadingSessionFixture Reading(string documentId)
    {
        var f = new ReadingSessionFixture();
        f.InLibrary(documentId);
        f.Session.Open(ReadingSessionFixture.PathOf(documentId));
        return f;
    }

    private static string DefaultWorkspaceOf(ReadingSessionFixture f, string documentId)
        => f.Store.All.Single(w => w.IsDefault && w.DefaultDocumentId == documentId).Id;

    private static CompositionTab Compose(ReadingSessionFixture f, string documentId, string id)
    {
        var composition = Make(id, DefaultWorkspaceOf(f, documentId));
        f.Compositions[id] = composition;
        return f.Session.OpenComposition(composition);
    }

    private static string Order(ReadingSessionFixture f) => string.Join(",", f.Session.Tabs.Tabs.Select(t => t.ItemId));

    [Fact]
    public void OpenComposition_WhileReadingStandalone_ShowsTabStripWithDocumentTabAndCompositionTab()
    {
        var f = Reading("A");
        Assert.False(f.Session.IsLightTabStripVisible);

        var tab = Compose(f, "A", "X");

        Assert.Equal("A,X", Order(f));
        Assert.Same(tab, f.Session.Tabs.ActiveTab);
        Assert.True(f.Session.IsLightTabStripVisible);
        Assert.False(f.Session.IsWorkspaceSession);
        Assert.Equal("A", f.Session.DocumentId);
    }

    [Fact]
    public void DocumentTab_InLightTabStrip_CannotBeClosed()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        var documentTab = f.Session.Tabs.Tabs[0];

        f.Session.Close(documentTab);

        Assert.False(documentTab.CanClose);
        Assert.Equal("A,X", Order(f));
    }

    [Fact]
    public void ClosingTheLastCompositionTab_ActivatesDocumentTabAndHidesStrip()
    {
        var f = Reading("A");
        var x = Compose(f, "A", "X");
        f.Log.Clear();

        f.Session.Close(x);

        Assert.Same(f.Session.Tabs.Tabs[0], f.Session.Tabs.ActiveTab);
        Assert.False(f.Session.IsLightTabStripVisible);
        Assert.False(f.Session.IsCompositionTabActive);
        Assert.False(f.Log.HasLoad());   // viewer standalone giữ nguyên Document, không nạp lại
        Assert.Equal("A", f.Session.DocumentId);
    }

    [Fact]
    public void ClosingActiveCompositionTab_ActivatesMostRecentlyUsedTab()
    {
        var f = Reading("A");
        var x = Compose(f, "A", "X");
        var y = Compose(f, "A", "Y");
        f.Session.Activate(x);
        f.Session.Activate(y);

        f.Session.Close(y);

        Assert.Same(x, f.Session.Tabs.ActiveTab);
    }

    [Fact]
    public void CompositionTabs_AreSavedUnderTheDefaultWorkspaceRow_WithoutTheDocumentTab()
    {
        var f = Reading("A");
        Compose(f, "A", "X");

        var saved = f.Store.OpenSets[DefaultWorkspaceOf(f, "A")];

        var row = Assert.Single(saved);
        Assert.Equal(("X", TabKind.Composition, true), (row.ItemId, row.Kind, row.IsActive));
    }

    [Fact]
    public void OpeningAnotherDocument_ClosesCompositionTabsAndHidesStrip()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        f.InLibrary("B");

        f.Session.Open(ReadingSessionFixture.PathOf("B"));

        Assert.Empty(f.Session.Tabs.Tabs);
        Assert.False(f.Session.IsLightTabStripVisible);
        Assert.Equal("B", f.Session.DocumentId);
        Assert.Equal(DefaultWorkspaceOf(f, "B"), f.Session.ActiveWorkspaceId);
    }

    [Fact]
    public void ReadingTheSameDocumentAgain_RestoresCompositionTabsWithOrderActiveTabAndScroll()
    {
        var f = Reading("A");
        var x = Compose(f, "A", "X");
        var y = Compose(f, "A", "Y");
        x.ScrollNorm = 0.4;
        y.ScrollNorm = 0.7;
        f.Session.Activate(x);
        f.InLibrary("B");
        f.Session.Open(ReadingSessionFixture.PathOf("B"));

        f.Session.Open(ReadingSessionFixture.PathOf("A"));

        Assert.Equal("A,X,Y", Order(f));
        var restoredX = Assert.IsType<CompositionTab>(f.Session.Tabs.Tabs[1]);
        var restoredY = Assert.IsType<CompositionTab>(f.Session.Tabs.Tabs[2]);
        Assert.Same(restoredX, f.Session.Tabs.ActiveTab);
        Assert.Equal((0.4, 0.7), (restoredX.ScrollNorm, restoredY.ScrollNorm));
        Assert.True(f.Session.IsLightTabStripVisible);
        Assert.False(f.Session.Tabs.Tabs[0].CanClose);
    }

    [Fact]
    public void ReadingTheSameDocumentAgain_WithDocumentTabActive_RestoresWithDocumentTabActive()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        f.Session.Activate(f.Session.Tabs.Tabs[0]);
        f.InLibrary("B");
        f.Session.Open(ReadingSessionFixture.PathOf("B"));

        f.Session.Open(ReadingSessionFixture.PathOf("A"));

        Assert.Same(f.Session.Tabs.Tabs[0], f.Session.Tabs.ActiveTab);
        Assert.False(f.Session.IsCompositionTabActive);
    }

    [Fact]
    public void ReadingSameDocumentAgain_SkipsCompositionsDeletedInTheMeantime()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        f.InLibrary("B");
        f.Session.Open(ReadingSessionFixture.PathOf("B"));
        f.Compositions.Remove("X");

        f.Session.Open(ReadingSessionFixture.PathOf("A"));

        Assert.Empty(f.Session.Tabs.Tabs);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void CompositionTabsOfDocumentA_DoNotAppearWhenReadingDocumentB()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        f.InLibrary("B");

        f.Session.Open(ReadingSessionFixture.PathOf("B"));
        f.Session.Open(ReadingSessionFixture.PathOf("B"));

        Assert.Empty(f.Session.Tabs.Tabs);
    }

    [Fact]
    public void Promote_LeavesCompositionTabsInDefaultWorkspace_AndNewWorkspaceHasOnlyTheDocumentTab()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        string defaultId = DefaultWorkspaceOf(f, "A");

        f.Session.Promote(new Workspace("ws-moi", "Mới", false, null, 1, 1), "Tài liệu A");

        var tab = Assert.Single(f.Session.Tabs.Tabs);
        Assert.IsType<DocumentTab>(tab);
        Assert.True(tab.CanClose);
        Assert.Equal("ws-moi", f.Session.ActiveWorkspaceId);
        Assert.False(f.Session.IsLightTabStripVisible);
        Assert.Equal("X", Assert.Single(f.Store.OpenSets[defaultId]).ItemId);
        Assert.DoesNotContain(f.Store.OpenSets["ws-moi"], s => s.Kind == TabKind.Composition);
    }

    [Fact]
    public void ReadingAgainAfterPromote_StillRestoresTheCompositionTab()
    {
        var f = Reading("A");
        Compose(f, "A", "X");
        f.Session.Promote(new Workspace("ws-moi", "Mới", false, null, 1, 1), "Tài liệu A");

        f.Session.Open(ReadingSessionFixture.PathOf("A"));

        Assert.Equal("A,X", Order(f));
        Assert.False(f.Session.IsWorkspaceSession);
    }

    [Fact]
    public void OpeningStandaloneFromANamedWorkspace_SavesItsOpenSetAndLeavesNoWorkspaceTabsBehind()
    {
        var f = new ReadingSessionFixture().InLibrary("A").InLibrary("B").InWorkspace();
        f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Open(ReadingSessionFixture.PathOf("A"));

        Assert.Equal("A,B", string.Join(",", f.Store.OpenSets[ReadingSessionFixture.WorkspaceId].Select(s => s.ItemId)));
        Assert.Empty(f.Session.Tabs.Tabs);
    }
}
