using System.Windows.Threading;
using PdfReaderApp.Tests.Fakes;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.ViewModels;

public sealed class OpenSetPersistenceTests
{
    private const string WorkspaceId = "ws-1";

    private static (OpenSetPersistence Persistence, TabSetViewModel Tabs, FakeWorkspaceStore Store) Create(
        Func<Dispatcher?> dispatcher, string? workspaceId = WorkspaceId, bool isWorkspaceSession = true)
    {
        var store = new FakeWorkspaceStore();
        var tabs = new TabSetViewModel();
        var persistence = new OpenSetPersistence(store, tabs, () => workspaceId, () => isWorkspaceSession)
        {
            DispatcherProvider = dispatcher,
        };
        return (persistence, tabs, store);
    }

    // Chạy message loop của dispatcher đủ lâu để timer debounce (400 ms) kịp bắn.
    private static void PumpFor(Dispatcher dispatcher, TimeSpan duration)
    {
        var frame = new DispatcherFrame();
        var stop = new DispatcherTimer(duration, DispatcherPriority.Background, (_, _) => frame.Continue = false, dispatcher);
        stop.Start();
        Dispatcher.PushFrame(frame);
        stop.Stop();
    }

    [Fact]
    public void Schedule_SavesImmediately_WhenNoDispatcher()
    {
        var (persistence, tabs, store) = Create(() => null);
        tabs.OpenOrActivate("docA", "A", "/a.pdf");
        store.OpenSets.Clear();

        persistence.Schedule();

        Assert.Equal("docA", Assert.Single(store.OpenSets[WorkspaceId]).DocumentId);
    }

    [Fact]
    public void Schedule_DoesNothing_WhenRestoring()
    {
        var (persistence, tabs, store) = Create(() => null);
        persistence.IsRestoring = true;

        tabs.OpenOrActivate("docA", "A", "/a.pdf");
        persistence.Schedule();

        Assert.Empty(store.OpenSets);
    }

    [Fact]
    public void Schedule_DoesNothing_WhenNotWorkspaceSession()
    {
        var (persistence, tabs, store) = Create(() => null, isWorkspaceSession: false);

        tabs.OpenOrActivate("docA", "A", "/a.pdf");
        persistence.Schedule();

        Assert.Empty(store.OpenSets);
    }

    [Fact]
    public void SaveNow_DoesNothing_WhenNoActiveWorkspace()
    {
        var (persistence, tabs, store) = Create(() => null, workspaceId: null);
        tabs.OpenOrActivate("docA", "A", "/a.pdf");

        persistence.SaveNow();

        Assert.Empty(store.OpenSets);
    }

    [Fact]
    public void SaveNow_PersistsOrderAndActiveTab()
    {
        var (persistence, tabs, store) = Create(() => null);
        tabs.OpenOrActivate("docA", "A", "/a.pdf");
        tabs.OpenOrActivate("docB", "B", "/b.pdf");

        persistence.SaveNow();

        var saved = store.OpenSets[WorkspaceId];
        Assert.Equal(["docA", "docB"], saved.Select(t => t.DocumentId), StringComparer.Ordinal);
        Assert.Equal([false, true], saved.Select(t => t.IsActive));
    }

    [Fact]
    public void Schedule_DebouncesIntoSingleSave_WhenDispatcherAvailable()
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        var (persistence, tabs, store) = Create(() => dispatcher);
        tabs.OpenOrActivate("docA", "A", "/a.pdf");
        store.OpenSets.Clear();

        persistence.Schedule();
        persistence.Schedule();
        persistence.Schedule();

        Assert.Empty(store.OpenSets);   // chưa ghi ngay: đang đợi debounce

        PumpFor(dispatcher, TimeSpan.FromMilliseconds(900));

        Assert.Equal("docA", Assert.Single(store.OpenSets[WorkspaceId]).DocumentId);
    }
}
