using PdfReaderApp.Models;
using PdfReaderApp.Core;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>Tab Bản soạn không đổi Phiên đọc (ADR 0009): Document, Chat, panel Notes và neo trích dẫn vẫn bám Tab Document gần nhất.</summary>
public class ReadingSessionCompositionTabTests
{
    private static Composition Make(string id = "c1")
        => new(id, ReadingSessionFixture.WorkspaceId, $"Bản {id}", CompositionKind.Summary, "# nội dung", "m", [], 1);

    [Fact]
    public void OpenComposition_ActivatesCompositionTab_WithoutTouchingTheReadingSession()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A", page: 4);
        f.Notes.PanelState = new(12, "note-a", true);

        var tab = f.Session.OpenComposition(Make());

        Assert.Same(tab, f.Session.Tabs.ActiveTab);
        Assert.True(f.Session.IsCompositionTabActive);
        Assert.Equal("A", f.Session.DocumentId);
        Assert.Equal(4, f.Session.CurrentPage);
        Assert.Equal(new NotesPanelState(12, "note-a", true), f.Notes.PanelState);
        Assert.Empty(f.Log.Entries);   // không huỷ Chat, không nạp Document, không reset Search, không đổi panel Notes
    }

    [Fact]
    public void OpenComposition_KeepsActiveSourcePassage()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        var passage = new SourcePassage("A", 0, ["đoạn"]);
        f.Session.ActiveSourcePassage = passage;

        f.Session.OpenComposition(Make());

        Assert.Equal(passage, f.Session.ActiveSourcePassage);
    }

    [Fact]
    public void OpenComposition_Twice_ActivatesTheExistingTabInsteadOfOpeningADuplicate()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var docTab = f.OpenTab("A");
        var first = f.Session.OpenComposition(Make());
        f.Session.Activate(docTab);

        var second = f.Session.OpenComposition(Make());

        Assert.Same(first, second);
        Assert.Equal(2, f.Session.Tabs.Tabs.Count);
        Assert.Same(second, f.Session.Tabs.ActiveTab);
    }

    [Fact]
    public void ReturningToTheSameDocumentTab_DoesNotReloadTheDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var docTab = f.OpenTab("A");
        f.Session.OpenComposition(Make());

        f.Session.Activate(docTab);

        Assert.False(f.Session.IsCompositionTabActive);
        Assert.False(f.Log.HasLoad());
        Assert.Equal("A", f.Session.DocumentId);
    }

    [Fact]
    public void SwitchingFromCompositionToAnotherDocumentTab_LoadsThatDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");
        f.Session.OpenComposition(Make());
        f.Log.Clear();

        f.Session.Activate(tabA);

        Assert.Equal("A", f.Session.DocumentId);
        Assert.True(f.Log.Has($"load:{ReadingSessionFixture.PathOf("A")}"));
    }

    [Fact]
    public void ClosingTheOnlyDocumentTabWhileCompositionIsActive_EmptiesTheReadingSession()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var docTab = f.OpenTab("A");
        f.Session.OpenComposition(Make());

        f.Session.Close(docTab);

        Assert.Null(f.Session.DocumentId);
        Assert.True(f.Session.IsCompositionTabActive);
    }

    [Fact]
    public void PageAndZoom_FollowTheLastDocumentTab_NotTheCompositionTab()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var docTab = f.OpenTab("A", page: 3);
        f.Session.OpenComposition(Make());

        f.Session.CurrentPage = 8;

        Assert.Equal(8, docTab.Page);
        Assert.Equal(8, f.Session.CurrentPage);
    }

    [Fact]
    public void OpenComposition_SavesCompositionTabInTheOpenSetWithItsKind()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");

        f.Session.OpenComposition(Make("c9"));

        var saved = f.Store.GetOpenTabs(ReadingSessionFixture.WorkspaceId);
        Assert.Equal([(TabKind.Document, "A", false), (TabKind.Composition, "c9", true)], saved.Select(s => (s.Kind, s.ItemId, s.IsActive)));
    }

    [Fact]
    public void RestoreOpenSet_SkipsSavedCompositionTabsForNow_AndStillRestoresDocuments()
    {
        var f = new ReadingSessionFixture().InLibrary("A");
        f.Store.SaveOpenTabs(ReadingSessionFixture.WorkspaceId,
        [
            new OpenTabState("A", 0, true, 1, 1.0, 0, 1, TabKind.Document),
            new OpenTabState("A", 1, false, 1, 1.0, 0, 1, TabKind.Composition),
        ]);

        f.Session.RestoreOpenSet(ReadingSessionFixture.WorkspaceId);

        Assert.All(f.Session.Tabs.Tabs, t => Assert.IsType<DocumentTab>(t));
        Assert.Single(f.Session.Tabs.Tabs);
    }
}
