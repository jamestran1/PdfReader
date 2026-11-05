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
}
