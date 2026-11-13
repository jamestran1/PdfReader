using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>
/// Hành vi đóng tab, chuyển sang tab MRU và khôi phục mà hai đường host tab phải giữ giống nhau (ADR 0009):
/// Workspace có tên (<see cref="TabSetViewModel"/>) và Tab Strip nhẹ của Default Workspace (<see cref="LightTabStripViewModel"/>).
/// Mỗi đường là một lớp con chạy lại toàn bộ test ở đây.
/// </summary>
public abstract class TabHostContractTests
{
    /// <summary>Dựng host đã có Tab Document "A" (active).</summary>
    protected abstract TabHost CreateHostWithDocument(out OpenTab document);

    private static CompositionTab Compose(TabHost host, string id)
        => host.OpenOrActivateComposition(new Composition(id, "ws", $"Bản {id}", CompositionKind.Summary, "# x", "m", [], 1));

    private static string Order(TabHost host) => string.Join(",", host.Tabs.Select(t => t.ItemId));

    [Fact]
    public void ClosingActiveTab_ActivatesTheMostRecentlyUsedRemainingTab()
    {
        var host = CreateHostWithDocument(out _);
        var x = Compose(host, "X");
        var y = Compose(host, "Y");
        host.ActivateTab(x);
        host.ActivateTab(y);

        host.Close(y);

        Assert.Same(x, host.ActiveTab);
        Assert.Equal("A,X", Order(host));
    }

    [Fact]
    public void ClosingTheLastCompositionTab_ActivatesTheDocumentTab()
    {
        var host = CreateHostWithDocument(out var document);
        var x = Compose(host, "X");

        host.Close(x);

        Assert.Same(document, host.ActiveTab);
    }

    [Fact]
    public void ClosingInactiveTab_KeepsTheActiveTab()
    {
        var host = CreateHostWithDocument(out _);
        var x = Compose(host, "X");
        var y = Compose(host, "Y");

        host.Close(x);

        Assert.Same(y, host.ActiveTab);
    }

    [Fact]
    public void SwitchingTabs_ThenClosingTheActiveOne_FollowsRecencyNotPosition()
    {
        var host = CreateHostWithDocument(out var document);
        var x = Compose(host, "X");
        var y = Compose(host, "Y");
        host.ActivateTab(x);
        host.ActivateTab(document);
        host.ActivateTab(y);

        host.Close(y);

        Assert.Same(document, host.ActiveTab);
    }

    [Fact]
    public void OpeningACompositionAlreadyOpen_ActivatesItWithoutDuplicating()
    {
        var host = CreateHostWithDocument(out var document);
        var x = Compose(host, "X");
        host.ActivateTab(document);

        var again = Compose(host, "X");

        Assert.Same(x, again);
        Assert.Equal("A,X", Order(host));
        Assert.Same(x, host.ActiveTab);
    }

    [Fact]
    public void RestoreTabs_ActivatesOnlyTheRequestedTab_WithASingleActiveTabChange()
    {
        var host = CreateHostWithDocument(out var document);
        host.Reset();
        var x = new CompositionTab(new Composition("X", "ws", "Bản X", CompositionKind.Summary, "# x", "m", [], 1));
        var y = new CompositionTab(new Composition("Y", "ws", "Bản Y", CompositionKind.Summary, "# y", "m", [], 1));
        int changes = 0;
        host.ActiveTabChanged += _ => changes++;

        host.RestoreTabs([document, x, y], "X");

        Assert.Equal("A,X,Y", Order(host));
        Assert.Same(x, host.ActiveTab);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void RestoredTabs_FollowTheRestoreOrderWhenTheActiveOneCloses()
    {
        var host = CreateHostWithDocument(out var document);
        host.Reset();
        var x = new CompositionTab(new Composition("X", "ws", "Bản X", CompositionKind.Summary, "# x", "m", [], 1));
        host.RestoreTabs([document, x], "X");

        host.Close(x);

        Assert.Same(document, host.ActiveTab);
    }

    [Fact]
    public void Move_ReordersCompositionTabsAmongThemselves()
    {
        var host = CreateHostWithDocument(out _);
        var x = Compose(host, "X");
        var y = Compose(host, "Y");

        host.Move(x, 2);
        Assert.Equal("A,Y,X", Order(host));

        host.Move(y, 2);
        Assert.Equal("A,X,Y", Order(host));
    }

    [Fact]
    public void Move_KeepsTheActiveTabAndTheMostRecentlyUsedOrder()
    {
        var host = CreateHostWithDocument(out _);
        var x = Compose(host, "X");
        var y = Compose(host, "Y");
        host.ActivateTab(x);
        int activeChanges = 0;
        host.ActiveTabChanged += _ => activeChanges++;

        host.Move(x, 0);   // thứ tự hiển thị X,A,Y; MRU vẫn X,Y,A

        Assert.Equal(0, activeChanges);
        Assert.Same(x, host.ActiveTab);

        // Đóng X: MRU chọn Y dù A mới là tab kề bên trong thứ tự hiển thị.
        host.Close(x);
        Assert.Same(y, host.ActiveTab);
        Assert.Equal("A,Y", Order(host));
    }

    [Fact]
    public void Move_RaisesTabsChangedOnceOnlyWhenTheOrderChanges()
    {
        var host = CreateHostWithDocument(out _);
        var x = Compose(host, "X");
        Compose(host, "Y");
        int changes = 0;
        host.TabsChanged += () => changes++;

        host.Move(x, 1);
        Assert.Equal(0, changes);

        host.Move(x, 2);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Move_ClampsTheTargetIndexAndIgnoresUnknownTabs()
    {
        var host = CreateHostWithDocument(out _);
        var x = Compose(host, "X");
        Compose(host, "Y");
        var stranger = new CompositionTab(new Composition("Z", "ws", "Bản Z", CompositionKind.Summary, "# z", "m", [], 1));

        host.Move(x, 99);
        host.Move(stranger, 0);

        Assert.Equal("A,Y,X", Order(host));
    }

    [Fact]
    public void Reset_EmptiesTheHostAndClearsTheActiveTab()
    {
        var host = CreateHostWithDocument(out _);
        Compose(host, "X");

        host.Reset();

        Assert.Empty(host.Tabs);
        Assert.Null(host.ActiveTab);
        Assert.False(host.HasTabs);
    }
}
