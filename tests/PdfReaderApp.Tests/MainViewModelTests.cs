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
        Assert.Equal(1, viewModel.CurrentPage);
        Assert.Equal(1, viewModel.TotalPages);
        Assert.Equal(1.0, viewModel.ZoomLevel);
    }

    [Fact]
    public void NextPageCommand_ShouldIncrementPage_WhenNotAtLastPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 1 };
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(2, viewModel.CurrentPage);
    }

    [Fact]
    public void NextPageCommand_ShouldNotIncrementPage_WhenAtLastPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 5 };
        viewModel.NextPageCommand.Execute(null);
        Assert.Equal(5, viewModel.CurrentPage);
    }

    [Fact]
    public void PreviousPageCommand_ShouldDecrementPage_WhenNotAtFirstPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 2 };
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.CurrentPage);
    }

    [Fact]
    public void PreviousPageCommand_ShouldNotDecrementPage_WhenAtFirstPage()
    {
        var viewModel = new MainViewModel { TotalPages = 5, CurrentPage = 1 };
        viewModel.PreviousPageCommand.Execute(null);
        Assert.Equal(1, viewModel.CurrentPage);
    }

    [Fact]
    public void ZoomInCommand_ShouldIncreaseZoomLevel()
    {
        var viewModel = new MainViewModel { ZoomLevel = 1.0 };
        viewModel.ZoomInCommand.Execute(null);
        Assert.Equal(1.2, viewModel.ZoomLevel, 1);
    }

    [Fact]
    public void ZoomOutCommand_ShouldDecreaseZoomLevel_WhenAboveMinimum()
    {
        var viewModel = new MainViewModel { ZoomLevel = 1.0 };
        viewModel.ZoomOutCommand.Execute(null);
        Assert.Equal(0.8, viewModel.ZoomLevel, 1);
    }

    [Fact]
    public void ZoomOutCommand_ShouldNotDecreaseZoomLevel_WhenAtMinimum()
    {
        var viewModel = new MainViewModel { ZoomLevel = 0.4 };
        viewModel.ZoomOutCommand.Execute(null);
        Assert.Equal(0.4, viewModel.ZoomLevel, 1);
    }
}