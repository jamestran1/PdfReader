using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp;

// Kéo-thả sắp lại Tab trên Tab Strip (#262). Ở đây chỉ nối sự kiện chuột với WPF và đo bề rộng chip;
// việc tính khe thả nằm ở TabDropPosition, việc đổi thứ tự và lưu ở ViewModel (CS-1).
public partial class MainWindow
{
    // Tab được bấm xuống nhưng chưa kéo đủ xa: click thường vẫn kích hoạt tab qua MouseBinding của chip.
    private OpenTab? _tabPressed;
    private Point _tabPressedAt;

    private void TabChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Nút đóng tab và các nút con tự xử lý chuột, không bắt đầu kéo từ đó.
        bool onButton = e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) is not null;
        _tabPressed = onButton ? null : (sender as FrameworkElement)?.DataContext as OpenTab;
        _tabPressedAt = e.GetPosition(this);
    }

    private void TabChip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tabPressed is not { } tab || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        if (Math.Abs(now.X - _tabPressedAt.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(now.Y - _tabPressedAt.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _tabPressed = null;
        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(OpenTab), tab), DragDropEffects.Move); }
        finally { TabDropIndicator.Visibility = Visibility.Collapsed; }
    }

    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (TryGetTabDrop(e) is not { } drop) return;

        e.Effects = DragDropEffects.Move;
        if (TabDropPosition.IsNoOp(drop.DragIndex, drop.Slot))
        {
            TabDropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        TabDropIndicator.Margin = new Thickness(
            Math.Max(0, TabDropPosition.SlotOffset(drop.Widths, drop.Slot) - (TabDropIndicator.Width / 2)), 6, 0, 6);
        TabDropIndicator.Visibility = Visibility.Visible;
    }

    private void TabStrip_DragLeave(object sender, DragEventArgs e)
        => TabDropIndicator.Visibility = Visibility.Collapsed;

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
        TabDropIndicator.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (TryGetTabDrop(e) is not { } drop || TabDropPosition.IsNoOp(drop.DragIndex, drop.Slot)) return;
        (DataContext as ViewModels.MainViewModel)?.MoveTab(drop.Tab, TabDropPosition.TargetIndex(drop.DragIndex, drop.Slot));
    }

    private readonly record struct TabDrop(OpenTab Tab, int DragIndex, int Slot, IReadOnlyList<double> Widths);

    // Tab đang kéo cùng khe thả dưới con trỏ; null khi dữ liệu kéo không phải một Tab của strip này.
    private TabDrop? TryGetTabDrop(DragEventArgs e)
    {
        if (e.Data.GetData(typeof(OpenTab)) is not OpenTab tab) return null;
        if (DataContext is not ViewModels.MainViewModel viewModel) return null;
        int dragIndex = viewModel.Session.CurrentTabs.Tabs.IndexOf(tab);
        if (dragIndex < 0) return null;

        var widths = new List<double>(TabStripItems.Items.Count);
        for (int index = 0; index < TabStripItems.Items.Count; index++)
            widths.Add((TabStripItems.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement)?.ActualWidth ?? 0);

        int slot = TabDropPosition.SlotAt(widths, e.GetPosition(TabStripItems).X);
        return new TabDrop(tab, dragIndex, slot, widths);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        for (var current = start; current is not null; current = current is Visual or System.Windows.Media.Media3D.Visual3D
            ? VisualTreeHelper.GetParent(current)
            : LogicalTreeHelper.GetParent(current))
        {
            if (current is T match) return match;
        }

        return null;
    }
}
