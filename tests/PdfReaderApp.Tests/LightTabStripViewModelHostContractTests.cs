using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Đường Default Workspace của <see cref="TabHostContractTests"/>, thêm các hành vi riêng của Tab Strip nhẹ.</summary>
public sealed class LightTabStripViewModelHostContractTests : TabHostContractTests
{
    protected override TabHost CreateHostWithDocument(out OpenTab document)
    {
        var host = new LightTabStripViewModel();
        document = host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        return host;
    }

    [Fact]
    public void DocumentTab_CannotBeClosed()
    {
        var host = new LightTabStripViewModel();
        var document = host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        var x = Compose(host, "X");

        host.Close(document);

        Assert.False(document.CanClose);
        Assert.Equal("A,X", Order(host));
        Assert.Same(x, host.ActiveTab);
    }

    [Fact]
    public void EnsureDocumentTab_CalledAgain_KeepsTheSameTabAtTheFront()
    {
        var host = new LightTabStripViewModel();
        var first = host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        Compose(host, "X");

        var second = host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));

        Assert.Same(first, second);
        Assert.Equal("A,X", Order(host));
    }

    [Fact]
    public void Strip_IsVisibleFromTwoTabs()
    {
        var host = new LightTabStripViewModel();
        host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        Assert.False(host.IsStripVisible);

        Compose(host, "X");

        Assert.True(host.IsStripVisible);
    }

    [Fact]
    public void DocumentTab_IsNeverHydrated()
    {
        var host = new LightTabStripViewModel();
        var document = host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        Compose(host, "X");
        host.ActivateTab(document);

        Assert.False(document.IsHydrated);
    }

    [Fact]
    public void DocumentTab_IsPinnedFirst_CannotBeMoved()
    {
        var host = new LightTabStripViewModel();
        var document = host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        Compose(host, "X");
        Compose(host, "Y");
        int changes = 0;
        host.TabsChanged += () => changes++;

        host.Move(document, 2);

        Assert.False(host.CanMove(document));
        Assert.Equal("A,X,Y", Order(host));
        Assert.Equal(0, changes);
    }

    [Fact]
    public void Move_NeverPutsACompositionTabAheadOfTheDocumentTab()
    {
        var host = new LightTabStripViewModel();
        host.EnsureDocumentTab(new DocumentRef("A", "A", "/a.pdf"));
        Compose(host, "X");
        var y = Compose(host, "Y");

        host.Move(y, 0);
        Assert.Equal("A,Y,X", Order(host));

        host.Move(y, -3);
        Assert.Equal("A,Y,X", Order(host));
        Assert.Equal(1, host.FirstMovableIndex);
        Assert.True(host.CanMove(y));
    }

    private static CompositionTab Compose(TabHost host, string id)
        => host.OpenOrActivateComposition(new Composition(id, "ws", $"Bản {id}", CompositionKind.Summary, "# x", "m", [], 1));

    private static string Order(TabHost host) => string.Join(",", host.Tabs.Select(t => t.ItemId));
}
