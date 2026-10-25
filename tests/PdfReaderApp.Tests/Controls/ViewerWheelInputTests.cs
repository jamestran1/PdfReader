using System.Windows;
using System.Windows.Input;
using PdfReaderApp.Controls.Viewer;
using PdfReaderApp.Core;

namespace PdfReaderApp.Tests.Controls;

public sealed class ViewerWheelInputTests
{
    private readonly FakeHost _host = new();
    private readonly ViewerWheelInput _input;

    public ViewerWheelInputTests() => _input = new ViewerWheelInput(_host);

    private sealed class FakeHost : IViewerHost
    {
        public bool HasDocument { get; set; } = true;
        public PdfViewMode ViewMode { get; set; } = PdfViewMode.Continuous;
        public bool ShowCover { get; set; }
        public int CurrentPage { get; set; } = 5;
        public int TotalPages { get; set; } = 10;
        public double ZoomLevel { get; set; } = 1.0;
        public double ScrollHorizontalOffset { get; set; }
        public double ScrollVerticalOffset { get; set; }
        public double ScrollableHeight { get; set; } = 1000;

        public int ZoomRequests { get; private set; }
        public (double H, double V)? ScrollRequest { get; private set; }
        public int ScrollToTopRequests { get; private set; }
        public int? PageRequest { get; private set; }

        public PdfDocumentSession? Session => null;
        public IReadOnlyList<PageSlot> Slots { get; } = [];
        public ICommand? AddNoteFromSelectionCommand => null;
        public bool IsEditorVisible => false;

        public void RequestZoom(double zoom)
        {
            ZoomRequests++;
            ZoomLevel = zoom;
        }

        public void RequestPage(int page)
        {
            PageRequest = page;
            CurrentPage = page;
        }

        public void RequestScroll(double horizontalOffset, double verticalOffset) => ScrollRequest = (horizontalOffset, verticalOffset);

        public void RequestScrollToTop() => ScrollToTopRequests++;

        public void RequestRepaint()
        {
        }

        public void ShowEditor(Point topLeft, Size size, string text)
        {
        }

        public void HideEditor()
        {
        }

        public void ShowAddNoteButton(Point topLeft)
        {
        }

        public void HideAddNoteButton()
        {
        }

        public void CapturePointer()
        {
        }

        public void ReleasePointer()
        {
        }
    }

    [Fact]
    public void CtrlWheelUp_ZoomsInAndKeepsPointUnderCursorStill()
    {
        _host.ScrollHorizontalOffset = 100;
        _host.ScrollVerticalOffset = 200;

        bool handled = _input.HandleWheel(120, isCtrlDown: true, new Point(50, 60));

        Assert.True(handled);
        Assert.Equal(1.1, _host.ZoomLevel, 6);
        Assert.Equal((100 + 50) / 1.0 * 1.1 - 50, _host.ScrollRequest!.Value.H, 6);
        Assert.Equal((200 + 60) / 1.0 * 1.1 - 60, _host.ScrollRequest!.Value.V, 6);
    }

    [Fact]
    public void CtrlWheel_AtZoomLimit_HandledButDoesNotChangeLayout()
    {
        _host.ZoomLevel = 5.0;

        bool handled = _input.HandleWheel(120, isCtrlDown: true, new Point(10, 10));

        Assert.True(handled);
        Assert.Equal(0, _host.ZoomRequests);
        Assert.Null(_host.ScrollRequest);
    }

    [Fact]
    public void CtrlWheel_WithoutDocument_HandledButDoesNothing()
    {
        _host.HasDocument = false;

        bool handled = _input.HandleWheel(120, isCtrlDown: true, new Point(10, 10));

        Assert.True(handled);
        Assert.Equal(0, _host.ZoomRequests);
    }

    [Fact]
    public void Wheel_InContinuousMode_IsNotHandled()
    {
        _host.ScrollVerticalOffset = _host.ScrollableHeight;

        Assert.False(_input.HandleWheel(-120, isCtrlDown: false, default));
        Assert.Null(_host.PageRequest);
    }

    [Fact]
    public void Wheel_InSinglePageModeWithoutDocument_IsNotHandled()
    {
        _host.ViewMode = PdfViewMode.SinglePage;
        _host.HasDocument = false;
        _host.ScrollVerticalOffset = _host.ScrollableHeight;

        Assert.False(_input.HandleWheel(-120, isCtrlDown: false, default));
    }

    [Fact]
    public void WheelDown_SinglePageAtBottom_AdvancesPageAndScrollsToTop()
    {
        _host.ViewMode = PdfViewMode.SinglePage;
        _host.ScrollVerticalOffset = _host.ScrollableHeight;

        bool handled = _input.HandleWheel(-120, isCtrlDown: false, default);

        Assert.True(handled);
        Assert.Equal(6, _host.PageRequest);
        Assert.Equal(1, _host.ScrollToTopRequests);
    }

    [Fact]
    public void WheelDown_SinglePageInsideTallPage_LeavesScrollingToViewer()
    {
        _host.ViewMode = PdfViewMode.SinglePage;
        _host.ScrollVerticalOffset = 300;

        Assert.False(_input.HandleWheel(-120, isCtrlDown: false, default));
        Assert.Null(_host.PageRequest);
    }

    [Fact]
    public void WheelUp_SinglePageAtTop_GoesBackWithoutScrollToTop()
    {
        _host.ViewMode = PdfViewMode.SinglePage;
        _host.ScrollVerticalOffset = 0;

        bool handled = _input.HandleWheel(120, isCtrlDown: false, default);

        Assert.True(handled);
        Assert.Equal(4, _host.PageRequest);
        Assert.Equal(0, _host.ScrollToTopRequests);
    }

    [Fact]
    public void WheelDown_FacingWithCoverAtBottom_JumpsToNextUnitFirstPage()
    {
        _host.ViewMode = PdfViewMode.Facing;
        _host.ShowCover = true;
        _host.CurrentPage = 2;
        _host.ScrollVerticalOffset = _host.ScrollableHeight;

        bool handled = _input.HandleWheel(-120, isCtrlDown: false, default);

        Assert.True(handled);
        Assert.Equal(4, _host.PageRequest);
    }
}
