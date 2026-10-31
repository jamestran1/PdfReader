using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Đường Default Workspace của <see cref="TabHostContractTests"/>, thêm các hành vi riêng của Tab Strip nhẹ.</summary>
public sealed class LightTabStripViewModelHostContractTests : TabHostContractTests
{
    protected override TabHost CreateHostWithDocument(out OpenTab document)
    {
        var host = new LightTabStripViewModel();
        document = host.EnsureDocumentTab("A", "A", "/a.pdf");
        return host;
    }

    [Fact]
    public void DocumentTab_CannotBeClosed()
    {
        var host = new LightTabStripViewModel();
        var document = host.EnsureDocumentTab("A", "A", "/a.pdf");
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
        var first = host.EnsureDocumentTab("A", "A", "/a.pdf");
        Compose(host, "X");

        var second = host.EnsureDocumentTab("A", "A", "/a.pdf");

        Assert.Same(first, second);
        Assert.Equal("A,X", Order(host));
    }

    [Fact]
    public void Strip_IsVisibleFromTwoTabs()
    {
        var host = new LightTabStripViewModel();
        host.EnsureDocumentTab("A", "A", "/a.pdf");
        Assert.False(host.IsStripVisible);

        Compose(host, "X");

        Assert.True(host.IsStripVisible);
    }

    [Fact]
    public void DocumentTab_IsNeverHydrated()
    {
        var host = new LightTabStripViewModel();
        var document = host.EnsureDocumentTab("A", "A", "/a.pdf");
        Compose(host, "X");
        host.ActivateTab(document);

        Assert.False(document.IsHydrated);
    }

    private static CompositionTab Compose(TabHost host, string id)
        => host.OpenOrActivateComposition(new Composition(id, "ws", $"Bản {id}", CompositionKind.Summary, "# x", "m", [], 1));

    private static string Order(TabHost host) => string.Join(",", host.Tabs.Select(t => t.ItemId));
}
