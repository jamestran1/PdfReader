using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

public class MainViewModelTests
{
    [Fact]
    public void MainViewModel_ShouldInitializeWithDefaultValues()
    {
        var viewModel = new MainViewModel();
        Assert.Equal("PDF Reader & AI", viewModel.WindowTitle);
    }
}