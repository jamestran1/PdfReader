using System.Globalization;
using PdfReaderApp;
using PdfReaderApp.Core;
using Xunit;

namespace PdfReaderApp.Tests;

public class NavDestinationToBoolConverterTests
{
    private static object? Run(NavDestination value, string parameter)
        => new NavDestinationToBoolConverter()
            .Convert(value, typeof(bool), parameter, CultureInfo.InvariantCulture);

    [Fact]
    public void Convert_ReturnsTrue_WhenDestinationMatchesParameter()
        => Assert.Equal(true, Run(NavDestination.Library, "Library"));

    [Fact]
    public void Convert_ReturnsFalse_WhenDestinationDiffersFromParameter()
        => Assert.Equal(false, Run(NavDestination.Library, "Reader"));
}
