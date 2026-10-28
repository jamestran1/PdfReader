using PdfReaderApp.Core;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionActivateTests
{
    [Fact]
    public void Activate_CancelsPendingChatBeforeLoadingTheNewDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Activate(tabA);

        int cancel = f.Log.IndexOf("chat.cancel");
        int load = f.Log.IndexOf($"load:{ReadingSessionFixture.PathOf("A")}");
        Assert.True(cancel >= 0, "chat.cancel không được gọi");
        Assert.True(load > cancel, "Document phải được nạp sau khi huỷ Chat");
    }

    [Fact]
    public void Activate_LoadsDocumentAtTheTabPage()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A", page: 7);
        f.OpenTab("B");

        f.Session.Activate(tabA);

        Assert.Equal("A", f.Session.DocumentId);
        Assert.Equal(7, f.Session.CurrentPage);
    }

    [Fact]
    public void Activate_RestoresNotesPanelStatePerTab()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.Notes.PanelState = new(120.5, "note-a", true);
        var tabB = f.OpenTab("B");

        f.Notes.PanelState = new(10, "note-b", false);
        f.Session.Activate(tabA);

        Assert.Equal(new NotesPanelState(120.5, "note-a", true), f.Notes.PanelState);

        f.Session.Activate(tabB);

        Assert.Equal(new NotesPanelState(10, "note-b", false), f.Notes.PanelState);
    }

    [Fact]
    public void Activate_ResetsSearchAndClearsSourcePassage()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");
        f.Session.ActiveSourcePassage = new SourcePassage("B", 0, ["đoạn nguồn"]);

        f.Session.Activate(tabA);

        Assert.True(f.Log.Has("search.reset"));
        Assert.Null(f.Session.ActiveSourcePassage);
    }

    [Fact]
    public void Activate_AfterLoad_ReloadsChatNotesAndStartsIndexingInOrder()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Activate(tabA);

        string[] expected =
        [
            "chat.cancel", "search.reset", $"load:{ReadingSessionFixture.PathOf("A")}",
            "chat.load", $"notes.load:{ReadingSessionFixture.WorkspaceId}", "notes.context",
            "search.clear", "indexing.start",
        ];
        Assert.Equal(expected, f.Log.Entries);
    }

    [Fact]
    public void Activate_WhenLoadFails_ReportsErrorKeepsWorkspaceAndClearsDocument()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");
        f.Loader.ThrowOnLoad = new InvalidOperationException("hỏng");

        f.Session.Activate(tabA);

        Assert.Null(f.Session.DocumentId);
        Assert.Equal(ReadingSessionFixture.WorkspaceId, f.Session.ActiveWorkspaceId);
        Assert.Single(f.Errors);
        Assert.Contains("hỏng", f.Errors[0], StringComparison.Ordinal);
        Assert.Equal("notes.load:null", f.Log.Entries.Last(e => e.StartsWith("notes.load", StringComparison.Ordinal)));
        Assert.Same(tabA, f.Session.Tabs.ActiveTab);
    }

    [Fact]
    public void Activate_PersistsOpenSetAfterTheSwitch()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        var tabA = f.OpenTab("A");
        f.OpenTab("B");

        f.Session.Activate(tabA);

        var saved = f.Store.GetOpenTabs(ReadingSessionFixture.WorkspaceId);
        Assert.Equal(2, saved.Count);
        Assert.Equal("A", saved.Single(t => t.IsActive).DocumentId);
    }
}
