using System.Windows;
using System.Windows.Input;
using PdfReaderApp.Core;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Con lăn của viewer: Ctrl + lăn zoom bám con trỏ, lăn thường ở chế độ một trang / hai trang lật trang khi đã chạm biên.
/// Tự đăng ký <c>PreviewMouseWheel</c>, còn mọi thay đổi (zoom, trang, cuộn) đi qua <see cref="IViewerHost"/> dưới dạng
/// yêu cầu. <see cref="HandleWheel"/> nhận giá trị thuần để test không cần dựng event WPF.
/// </summary>
internal sealed class ViewerWheelInput(IViewerHost host)
{
    private UIElement? _scope;
    private IInputElement? _scrollArea;

    /// <summary>Đăng ký event; <paramref name="scope"/> nhận con lăn, toạ độ chuột tính theo <paramref name="scrollArea"/>.</summary>
    public void Attach(UIElement scope, IInputElement scrollArea)
    {
        _scope = scope;
        _scrollArea = scrollArea;
        scope.PreviewMouseWheel += OnPreviewMouseWheel;
    }

    public void Detach()
    {
        if (_scope != null) _scope.PreviewMouseWheel -= OnPreviewMouseWheel;
    }

    /// <summary>Trả về true khi con lăn đã được xử lý (event không đi tiếp).</summary>
    public bool HandleWheel(int delta, bool isCtrlDown, Point mousePosition)
    {
        if (isCtrlDown)
        {
            ZoomAtCursor(delta, mousePosition);
            return true;
        }
        return host.ViewMode is PdfViewMode.SinglePage or PdfViewMode.Facing && host.HasDocument && NavigatePage(delta);
    }

    private void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (HandleWheel(e.Delta, Keyboard.Modifiers.HasFlag(ModifierKeys.Control), e.GetPosition(_scrollArea)))
            e.Handled = true;
    }

    private void ZoomAtCursor(int delta, Point mousePosition)
    {
        if (!host.HasDocument) return;

        var anchor = new WheelZoomAnchor(host.ScrollHorizontalOffset, host.ScrollVerticalOffset, mousePosition.X, mousePosition.Y);
        double oldZoom = host.ZoomLevel;

        if (ViewerWheelMath.NextZoom(oldZoom, delta) is not double newZoom) return;

        // Control ghi ZoomLevel (kéo theo RefreshLayout) rồi dựng lại layout để ScrollViewer biết kích thước canvas mới.
        host.RequestZoom(newZoom);

        var (newHOffset, newVOffset) = ViewerWheelMath.AnchoredOffsets(anchor, oldZoom, newZoom);
        host.RequestScroll(newHOffset, newVOffset);
    }

    private bool NavigatePage(int delta)
    {
        // Đơn vị hiện tại lấp đầy khung. Nếu cao hơn khung thì lăn trong nó trước, chỉ sang đơn vị kế/trước khi đã ở biên.
        var decision = ViewerWheelMath.ResolvePageChange(new WheelPageContext(
            delta,
            AtTop: host.ScrollVerticalOffset <= 0.5,
            AtBottom: host.ScrollVerticalOffset >= host.ScrollableHeight - 0.5,
            host.CurrentPage, host.TotalPages, host.ViewMode == PdfViewMode.Facing, host.ShowCover));
        if (!decision.Handled) return false;

        if (decision.NewPage is int page) host.RequestPage(page);
        if (decision.ScrollToTop) host.RequestScrollToTop();
        return true;
    }
}
