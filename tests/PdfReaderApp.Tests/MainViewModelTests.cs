using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;
using Xunit;

namespace PdfReaderApp.Tests;

public class MainViewModelTests
{
    [Fact]
    public void MainViewModel_ShouldInitializeWithDefaultValues()
    {
        var viewModel = new MainViewModel();
        Assert.Equal("Ultimate PDF Reader & Editor", viewModel.WindowTitle);
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

    [Fact]
    public void OnSearchQueryChanged_ClearsPageHighlight()
    {
        var vm = new MainViewModel();
        vm.SelectedSearchQuery = "abc";
        vm.SearchQuery = "x";
        Assert.Equal(string.Empty, vm.SelectedSearchQuery);
    }

    [Fact]
    public void SearchQuery_SetEmpty_ClearsResultsAndExecutedQuery()
    {
        var vm = new MainViewModel();
        vm.SearchQuery = "abc";
        vm.ExecutedSearchQuery = "abc";
        vm.SearchQuery = "";
        Assert.Empty(vm.SearchResults);
        Assert.Equal(string.Empty, vm.ExecutedSearchQuery);
    }

    [Fact]
    public void ClearSearchCommand_ClearsQueryAndResults()
    {
        var vm = new MainViewModel();
        vm.SearchQuery = "abc";
        vm.ClearSearchCommand.Execute(null);
        Assert.Equal(string.Empty, vm.SearchQuery);
        Assert.Empty(vm.SearchResults);
    }

    [Fact]
    public void SelectSearchResult_SetsPageAndHighlightQuery()
    {
        var vm = new MainViewModel { SearchQuery = "hành" };
        var result = new SearchResult(2, "snip", 1);
        vm.SelectSearchResultCommand.Execute(result);
        Assert.Equal(3, vm.CurrentPage);
        Assert.Equal("hành", vm.SelectedSearchQuery);
    }

    [Fact]
    public void ViewMode_DefaultsToContinuous()
    {
        Assert.Equal(PdfViewMode.Continuous, new MainViewModel().ViewMode);
    }

    [Fact]
    public void ShowCoverSeparately_DefaultsToTrue()
    {
        Assert.True(new MainViewModel().ShowCoverSeparately);
    }

    [Fact]
    public void ViewMode_CanChange()
    {
        var vm = new MainViewModel { ViewMode = PdfViewMode.Facing };
        Assert.Equal(PdfViewMode.Facing, vm.ViewMode);
    }

    [Fact]
    public void FirstPageCommand_GoesToPageOne()
    {
        var vm = new MainViewModel { TotalPages = 10, CurrentPage = 7 };
        vm.FirstPageCommand.Execute(null);
        Assert.Equal(1, vm.CurrentPage);
    }

    [Fact]
    public void LastPageCommand_GoesToLastPage()
    {
        var vm = new MainViewModel { TotalPages = 10, CurrentPage = 3 };
        vm.LastPageCommand.Execute(null);
        Assert.Equal(10, vm.CurrentPage);
    }

    [Fact]
    public void ShowLibraryViewCommand_SetsShowLibraryTrue()
    {
        var vm = new MainViewModel();
        vm.ShowLibrary = false;
        vm.ShowLibraryViewCommand.Execute(null);
        Assert.True(vm.ShowLibrary);
    }

    [Fact]
    public void ShowLibrary_DefaultsTrue()
    {
        Assert.True(new MainViewModel().ShowLibrary);
    }
}
