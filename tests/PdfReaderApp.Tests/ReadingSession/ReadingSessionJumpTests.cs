using System.IO;
using PdfReaderApp.Core;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ReadingSession;

public class ReadingSessionJumpTests
{
    private static JumpRequest To(string documentId, int pageIndex, JumpOrigin origin, SourcePassage? passage = null)
        => new(documentId, pageIndex, origin, passage);

    [Fact]
    public void Jump_FromSearch_ToOtherDocument_ActivatesItsTabAtTargetPageAndKeepsSearch()
    {
        var f = new ReadingSessionFixture().InWorkspace().InLibrary("B");
        f.OpenTab("A");

        f.Session.Jump(To("B", 4, JumpOrigin.Search));

        Assert.Equal("B", f.Session.Tabs.ActiveTab!.DocumentId);
        Assert.Equal(5, f.Session.CurrentPage);
        Assert.False(f.Log.Has("search.reset"));
        Assert.False(f.Log.Has("search.clear"));
    }

    [Fact]
    public void Jump_FromSearch_DoesNotLeaveSearchKeptForALaterManualTabSwitch()
    {
        var f = new ReadingSessionFixture().InWorkspace().InLibrary("B");
        var tabA = f.OpenTab("A");
        f.Session.Jump(To("B", 0, JumpOrigin.Search));
        f.Log.Clear();

        f.Session.Activate(tabA);

        Assert.True(f.Log.Has("search.reset"));
    }

    [Fact]
    public void Jump_FromNote_ToOtherDocument_ActivatesItsTabAtNotePageAndResetsSearch()
    {
        var f = new ReadingSessionFixture().InWorkspace().InLibrary("B");
        f.OpenTab("A");

        f.Session.Jump(To("B", 2, JumpOrigin.Note));

        Assert.Equal("B", f.Session.Tabs.ActiveTab!.DocumentId);
        Assert.Equal(3, f.Session.CurrentPage);
        Assert.True(f.Log.Has("search.reset"));
    }

    [Fact]
    public void Jump_ToDocumentWithOpenTab_ActivatesItInsteadOfOpeningAnother()
    {
        var f = new ReadingSessionFixture().InWorkspace().InLibrary("B");
        f.OpenTab("B", page: 1);
        f.OpenTab("A");

        f.Session.Jump(To("B", 6, JumpOrigin.Note));

        Assert.Equal(2, f.Session.Tabs.Tabs.Count);
        Assert.Equal("B", f.Session.Tabs.ActiveTab!.DocumentId);
        Assert.Equal(7, f.Session.CurrentPage);
    }

    [Fact]
    public void Jump_FromCitation_SetsSourcePassageAfterTheDocumentLoads()
    {
        var f = new ReadingSessionFixture().InWorkspace().InLibrary("B");
        f.OpenTab("A");
        var passage = new SourcePassage("B", 3, ["đoạn nguồn"]);

        f.Session.Jump(To("B", 3, JumpOrigin.Citation, passage));

        Assert.Equal("B", f.Session.Tabs.ActiveTab!.DocumentId);
        Assert.Equal(4, f.Session.CurrentPage);
        Assert.Same(passage, f.Session.ActiveSourcePassage);
    }

    [Fact]
    public void Jump_FromCitation_ToDocumentRemovedFromLibrary_DoesNothingAndClearsSourcePassage()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");
        f.Session.ActiveSourcePassage = new SourcePassage("A", 0, ["cũ"]);

        f.Session.Jump(To("gone", 1, JumpOrigin.Citation, new SourcePassage("gone", 1, ["mới"])));

        Assert.Equal("A", f.Session.Tabs.ActiveTab!.DocumentId);
        Assert.Single(f.Session.Tabs.Tabs);
        Assert.Null(f.Session.ActiveSourcePassage);
        Assert.False(f.Log.HasLoad());
    }

    [Fact]
    public void Jump_FromNote_ToDocumentRemovedFromLibrary_DoesNothing()
    {
        var f = new ReadingSessionFixture().InWorkspace();
        f.OpenTab("A");

        f.Session.Jump(To("gone", 1, JumpOrigin.Note));

        Assert.Equal("A", f.Session.Tabs.ActiveTab!.DocumentId);
        Assert.False(f.Log.HasLoad());
        Assert.False(f.Log.Has("search.reset"));
    }

    [Fact]
    public void Jump_FromCitation_WhenLoadFails_ReportsErrorAndStillSetsSourcePassage()
    {
        // Hành vi hiện có được ghim (không sửa): Đoạn nguồn đặt theo việc Document còn trong Library, không theo kết quả nạp.
        var f = new ReadingSessionFixture().InWorkspace().InLibrary("B");
        f.OpenTab("A");
        f.Loader.ThrowOnLoad = new IOException("hỏng");
        var passage = new SourcePassage("B", 0, ["đoạn"]);

        f.Session.Jump(To("B", 0, JumpOrigin.Citation, passage));

        Assert.Single(f.Errors);
        Assert.Same(passage, f.Session.ActiveSourcePassage);
    }

    [Fact]
    public void Jump_WhenReadingStandalone_ReplacesTheDocumentWithoutCreatingATab()
    {
        var f = new ReadingSessionFixture().InLibrary("B");

        f.Session.Jump(To("B", 8, JumpOrigin.Note));

        Assert.False(f.Session.IsWorkspaceSession);
        Assert.Empty(f.Session.Tabs.Tabs);
        Assert.Equal("B", f.Session.DocumentId);
        Assert.Equal(ReadingSessionFixture.PathOf("B"), f.Session.FilePath);
        Assert.Equal(9, f.Session.CurrentPage);
    }

    [Fact]
    public void Jump_FromSearch_WhenReadingStandalone_KeepsSearchResults()
    {
        var f = new ReadingSessionFixture().InLibrary("B");

        f.Session.Jump(To("B", 0, JumpOrigin.Search));

        Assert.False(f.Log.Has("search.clear"));
    }

    [Fact]
    public void Jump_FromNote_WhenReadingStandalone_ClearsSearchResults()
    {
        var f = new ReadingSessionFixture().InLibrary("B");

        f.Session.Jump(To("B", 0, JumpOrigin.Note));

        Assert.True(f.Log.Has("search.clear"));
    }

    [Fact]
    public void Jump_WhenReadingStandalone_KeepsThePreviousDocumentsWorkspaceScope()
    {
        // Hành vi hiện có được ghim (không sửa): Document nhảy tới thừa hưởng id Default Workspace của Document trước
        // thay vì có Default Workspace riêng của nó.
        var f = new ReadingSessionFixture().InLibrary("B");
        f.InLibrary("A").Session.Open(ReadingSessionFixture.PathOf("A"));
        string scopeOfA = f.Session.ActiveWorkspaceId!;

        f.Session.Jump(To("B", 0, JumpOrigin.Note));

        Assert.Equal(scopeOfA, f.Session.ActiveWorkspaceId);
    }
}
