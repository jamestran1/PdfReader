using System.Globalization;
using PdfReaderApp.Converters;

namespace PdfReaderApp.Tests.Converters;

public class NoteIsSelectedConverterTests
{
    private static object Convert(params object?[] values)
        => new NoteIsSelectedConverter().Convert([.. values.Select(v => v ?? System.Windows.DependencyProperty.UnsetValue)], typeof(bool), null!, CultureInfo.InvariantCulture);

    [Fact]
    public void Convert_SameId_IsSelected() => Assert.Equal(true, Convert("n1", "n1"));

    [Fact]
    public void Convert_DifferentId_IsNotSelected() => Assert.Equal(false, Convert("n1", "n2"));

    [Fact]
    public void Convert_NothingSelected_IsNotSelected() => Assert.Equal(false, Convert("n1", null));
}
