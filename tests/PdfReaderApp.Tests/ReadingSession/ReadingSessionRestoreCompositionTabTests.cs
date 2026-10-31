using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>Open Set của Workspace có tên nhớ cả Tab Bản soạn (ADR 0009): thứ tự xen kẽ, tab active, vị trí cuộn.</summary>
public class ReadingSessionRestoreCompositionTabTests
{
    private const string Target = "ws-target";

    private static OpenTabState SavedDocument(string id, int order, bool active = false, int page = 1)
        => new(id, order, active, page, 1.0, 0, 0);

    private static OpenTabState SavedComposition(string id, int order, bool active = false, double scroll = 0)
        => new(id, order, active, 1, 1.0, scroll, 0, TabKind.Composition);

    private static ReadingSessionFixture WithComposition(string id, params string[] documentIds)
    {
        var f = new ReadingSessionFixture();
        foreach (var d in documentIds) f.InLibrary(d);
        if (id.Length > 0)
            f.Compositions[id] = new Composition(id, Target, $"Bản {id}", CompositionKind.Summary, "# nội dung", "m", [], 1);
        return f;
    }

    [Fact]
    public void RestoreOpenSet_InterleavedCompositionTab_RestoresOrderActiveTabAndScroll()
    {
        var f = WithComposition("X", "A", "B");
        f.Store.OpenSets[Target] = [SavedDocument("A", 0), SavedComposition("X", 1, active: true, scroll: 0.5), SavedDocument("B", 2)];

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A,X,B", string.Join(",", f.Session.Tabs.Tabs.Select(t => t.ItemId)));
        var x = Assert.IsType<CompositionTab>(f.Session.Tabs.Tabs[1]);
        Assert.Same(x, f.Session.Tabs.ActiveTab);
        Assert.Equal(0.5, x.ScrollNorm);
        Assert.True(f.Session.IsCompositionTabActive);
    }

    [Fact]
    public void RestoreOpenSet_ActiveCompositionTab_ReadingSessionKeepsNearestDocument()
    {
        var f = WithComposition("X", "A", "B");
        f.Store.OpenSets[Target] = [SavedDocument("A", 0, page: 3), SavedComposition("X", 1, active: true), SavedDocument("B", 2)];

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A", f.Session.DocumentId);
        Assert.Equal(3, f.Session.CurrentPage);
        Assert.Equal([ReadingSessionFixture.PathOf("A")], f.Loader.LoadedPaths);
    }

    [Fact]
    public void RestoreOpenSet_DeletedComposition_IsSkippedWithoutError()
    {
        var f = WithComposition("", "A");
        f.Store.OpenSets[Target] = [SavedComposition("X", 0), SavedDocument("A", 1, active: true)];

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A", Assert.Single(f.Session.Tabs.Tabs).ItemId);
        Assert.Empty(f.Errors);
    }

    [Fact]
    public void RestoreOpenSet_ActiveCompositionDeleted_FallsBackToFirstRestoredTab()
    {
        var f = WithComposition("", "A");
        f.Store.OpenSets[Target] = [SavedDocument("A", 0), SavedComposition("X", 1, active: true)];

        f.Session.RestoreOpenSet(Target);

        Assert.Equal("A", f.Session.Tabs.ActiveTab?.ItemId);
    }

    [Fact]
    public void RestoreOpenSet_OnlyCompositionTab_RestoresItWithEmptyReadingSession()
    {
        var f = WithComposition("X");
        f.Store.OpenSets[Target] = [SavedComposition("X", 0, active: true)];

        f.Session.RestoreOpenSet(Target);

        Assert.IsType<CompositionTab>(f.Session.Tabs.ActiveTab);
        Assert.Null(f.Session.DocumentId);
    }

    [Fact]
    public void RestoreOpenSet_CompositionRowIsSavedBackWithPageOneZoomOneAndScroll()
    {
        var f = WithComposition("X", "A");
        f.Store.OpenSets[Target] = [SavedDocument("A", 0), SavedComposition("X", 1, active: true, scroll: 0.25)];

        f.Session.RestoreOpenSet(Target);

        var row = f.Store.OpenSets[Target].Single(s => s.Kind == TabKind.Composition);
        Assert.Equal(("X", 1, 1.0, 0.25, true), (row.ItemId, row.Page, row.Zoom, row.ScrollNorm, row.IsActive));
    }
}
