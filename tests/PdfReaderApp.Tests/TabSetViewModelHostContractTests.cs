using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests;

/// <summary>Đường Workspace có tên của <see cref="TabHostContractTests"/>.</summary>
public sealed class TabSetViewModelHostContractTests : TabHostContractTests
{
    protected override TabHost CreateHostWithDocument(out OpenTab document)
    {
        var host = new TabSetViewModel();
        document = host.OpenOrActivate("A", "A", "/a.pdf");
        return host;
    }

    [Fact]
    public void NamedWorkspace_EveryTabIsFreeToMoveToTheFront()
    {
        var host = new TabSetViewModel();
        host.OpenOrActivate("A", "A", "/a.pdf");
        var b = host.OpenOrActivate("B", "B", "/b.pdf");

        host.Move(b, 0);

        Assert.Equal(0, host.FirstMovableIndex);
        Assert.True(host.CanMove(b));
        Assert.Equal("B,A", string.Join(",", host.Tabs.Select(t => t.ItemId)));
    }
}
