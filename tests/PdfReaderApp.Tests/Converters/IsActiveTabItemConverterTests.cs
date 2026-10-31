using System.Globalization;
using PdfReaderApp.Converters;
using PdfReaderApp.Models;

namespace PdfReaderApp.Tests.Converters;

public class IsActiveTabItemConverterTests
{
    private static readonly Composition Comp = new("same", "ws", "T", CompositionKind.Summary, "x", "m", [], 1);

    private static object? Convert(object?[] values, string kind)
        => new IsActiveTabItemConverter().Convert(values!, typeof(bool), kind, CultureInfo.InvariantCulture);

    [Fact]
    public void DocumentRow_IsActiveOnlyWhenTheActiveTabIsThatDocumentTab()
    {
        var documentTab = new DocumentTab("same", "D", "/d.pdf");

        Assert.Equal(true, Convert(["same", documentTab], "Document"));
        Assert.Equal(false, Convert(["other", documentTab], "Document"));
    }

    [Fact]
    public void RowOfOneKind_IsNotActiveWhenTheActiveTabIsOfTheOtherKindWithTheSameId()
    {
        var compositionTab = new CompositionTab(Comp);

        Assert.Equal(false, Convert(["same", compositionTab], "Document"));
        Assert.Equal(true, Convert(["same", compositionTab], "Composition"));
    }

    [Fact]
    public void NoActiveTab_IsNotActive()
    {
        Assert.Equal(false, Convert(["same", null], "Document"));
    }
}
