using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.ReadingSession;

/// <summary>
/// Đóng tab, chuyển sang tab MRU và khôi phục phải giống nhau ở cả hai đường host (ADR 0009), nên mỗi test chạy trên cả hai.
/// </summary>
public class ReadingSessionHostPathTests
{
    private const string NamedId = ReadingSessionFixture.WorkspaceId;

    private sealed class Arranged
    {
        public required ReadingSessionFixture F { get; init; }
        public required HostPath Path { get; init; }
        public required string WorkspaceId { get; init; }

        public IEnumerable<OpenTab> Tabs => F.Session.CurrentTabs.Tabs;
        public string CompositionOrder => string.Join(",", Tabs.OfType<CompositionTab>().Select(t => t.CompositionId));

        public CompositionTab Compose(string id)
        {
            var composition = new Composition(id, WorkspaceId, $"Bản {id}", CompositionKind.Summary, "# nội dung", "m", [], 1);
            F.Compositions[id] = composition;
            return F.Session.OpenComposition(composition);
        }

        public OpenTab Document => Tabs.OfType<DocumentTab>().Single();

        /// <summary>Rời Workspace (đọc Document B, hoặc vào Workspace khác) rồi quay lại đúng Workspace này.</summary>
        public void LeaveAndReturn()
        {
            if (Path == HostPath.NamedWorkspace)
            {
                F.Session.RestoreOpenSet("ws-khac");
                F.Session.RestoreOpenSet(NamedId);
            }
            else
            {
                F.Session.Open(ReadingSessionFixture.PathOf("B"));
                F.Session.Open(ReadingSessionFixture.PathOf("A"));
            }
        }
    }

    private static Arranged Arrange(HostPath path)
    {
        var f = new ReadingSessionFixture().InLibrary("A").InLibrary("B");
        if (path == HostPath.NamedWorkspace)
        {
            f.InWorkspace();
            f.OpenTab("A");
            return new Arranged { F = f, Path = path, WorkspaceId = NamedId };
        }

        f.Session.Open(ReadingSessionFixture.PathOf("A"));
        string defaultId = f.Store.All.Single(w => w.IsDefault && w.DefaultDocumentId == "A").Id;
        return new Arranged { F = f, Path = path, WorkspaceId = defaultId };
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void ClosingTheActiveCompositionTab_ActivatesTheMostRecentlyUsedTab(HostPath path)
    {
        var a = Arrange(path);
        var x = a.Compose("X");
        var y = a.Compose("Y");
        a.F.Session.Activate(x);
        a.F.Session.Activate(y);

        a.F.Session.Close(y);

        Assert.Same(x, a.F.Session.CurrentTabs.ActiveTab);
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void SwitchingTabs_ThenClosing_FollowsRecencyNotPosition(HostPath path)
    {
        var a = Arrange(path);
        var x = a.Compose("X");
        var y = a.Compose("Y");
        a.F.Session.Activate(x);
        a.F.Session.Activate(a.Document);
        a.F.Session.Activate(y);

        a.F.Session.Close(y);

        Assert.Same(a.Document, a.F.Session.CurrentTabs.ActiveTab);
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void ClosingTheLastCompositionTab_ReturnsToTheDocument_WithoutReloadingIt(HostPath path)
    {
        var a = Arrange(path);
        var x = a.Compose("X");
        a.F.Log.Clear();

        a.F.Session.Close(x);

        Assert.Same(a.Document, a.F.Session.CurrentTabs.ActiveTab);
        Assert.False(a.F.Session.IsCompositionTabActive);
        Assert.False(a.F.Log.HasLoad());
        Assert.Equal("A", a.F.Session.DocumentId);
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void OpeningAComposition_DoesNotChangeTheReadingSession(HostPath path)
    {
        var a = Arrange(path);

        a.Compose("X");

        Assert.True(a.F.Session.IsCompositionTabActive);
        Assert.Equal("A", a.F.Session.DocumentId);
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void LeavingAndReturning_RestoresCompositionTabsWithOrderActiveTabAndScroll(HostPath path)
    {
        var a = Arrange(path);
        var x = a.Compose("X");
        var y = a.Compose("Y");
        x.ScrollNorm = 0.4;
        y.ScrollNorm = 0.7;
        a.F.Session.Activate(x);

        a.LeaveAndReturn();

        Assert.Equal("X,Y", a.CompositionOrder);
        var restored = a.Tabs.OfType<CompositionTab>().ToList();
        Assert.Same(restored[0], a.F.Session.CurrentTabs.ActiveTab);
        Assert.Equal((0.4, 0.7), (restored[0].ScrollNorm, restored[1].ScrollNorm));
        Assert.Equal("A", a.F.Session.DocumentId);
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void LeavingAndReturning_RestoresRecencyForClosingTheActiveTab(HostPath path)
    {
        var a = Arrange(path);
        a.Compose("X");
        a.Compose("Y");

        a.LeaveAndReturn();
        var active = a.F.Session.CurrentTabs.ActiveTab!;
        a.F.Session.Close(active);

        Assert.Equal("A", a.F.Session.DocumentId);
        Assert.NotNull(a.F.Session.CurrentTabs.ActiveTab);
        Assert.DoesNotContain(active, a.Tabs);
    }

    [Theory]
    [InlineData(HostPath.NamedWorkspace)]
    [InlineData(HostPath.DefaultWorkspace)]
    public void LeavingAndReturning_SkipsCompositionsDeletedInTheMeantime(HostPath path)
    {
        var a = Arrange(path);
        a.Compose("X");
        a.Compose("Y");
        a.F.Session.RestoreOpenSet("ws-tam");   // rời bằng đường chung để xoá X trước khi quay lại
        a.F.Compositions.Remove("X");
        if (path == HostPath.DefaultWorkspace) a.F.Session.Open(ReadingSessionFixture.PathOf("A"));
        else a.F.Session.RestoreOpenSet(NamedId);

        Assert.Equal("Y", a.CompositionOrder);
        Assert.Empty(a.F.Errors);
    }
}
