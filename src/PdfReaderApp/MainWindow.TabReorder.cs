using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

namespace PdfReaderApp;

// Kéo-thả sắp lại Tab trên Tab Strip (#262). Ở đây chỉ nối sự kiện chuột với WPF và đo bề rộng chip;
// việc tính khe thả nằm ở TabDropPosition, việc đổi thứ tự và lưu ở ViewModel (CS-1).
// Click kích hoạt tab ở lúc thả chuột (không phải lúc nhấn) để kéo một tab không đổi Tab active.
public partial class MainWindow
{
    // Tab đang được nhấn chuột và chưa kéo: thả chuột trên chính chip đó là click.
    private OpenTab? _tabPressed;
    private Point _tabPressedAt;

    private ViewModels.MainViewModel? TabsViewModel => DataContext as ViewModels.MainViewModel;

    private void TabChip_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Nút đóng tab và các nút con tự xử lý chuột, không bắt đầu kéo hay click từ đó.
        bool onButton = e.OriginalSource is DependencyObject source && FindAncestor<ButtonBase>(source) is not null;
        _tabPressed = onButton ? null : (sender as FrameworkElement)?.DataContext as OpenTab;
        _tabPressedAt = e.GetPosition(this);
    }

    private void TabChip_MouseMove(object sender, MouseEventArgs e)
    {
        if (_tabPressed is not { } tab || e.LeftButton != MouseButtonState.Pressed) return;

        var now = e.GetPosition(this);
        if (!TabDropPosition.IsDragGesture(
                now.X - _tabPressedAt.X, now.Y - _tabPressedAt.Y,
                SystemParameters.MinimumHorizontalDragDistance, SystemParameters.MinimumVerticalDragDistance))
        {
            return;
        }

        _tabPressed = null;
        if (TabsViewModel?.CanDragTab(tab) != true) return;   // Tab Document ghim ở đầu Tab Strip nhẹ

        try { DragDrop.DoDragDrop((DependencyObject)sender, new DataObject(typeof(OpenTab), tab), DragDropEffects.Move); }
        finally { TabDropIndicator.Visibility = Visibility.Collapsed; }
    }

    private void TabChip_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        var tab = (sender as FrameworkElement)?.DataContext as OpenTab;
        bool isClick = tab is not null && ReferenceEquals(tab, _tabPressed);
        _tabPressed = null;
        if (isClick) TabsViewModel?.ActivateTabCommand.Execute(tab);
    }

    private void TabStrip_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.None;
        e.Handled = true;
        if (TryGetTabDrop(e) is not { } drop) return;

        e.Effects = DragDropEffects.Move;
        if (TabsViewModel!.IsNoOpDrop(drop.Tab, drop.Slot))
        {
            TabDropIndicator.Visibility = Visibility.Collapsed;
            return;
        }

        var margin = TabDropIndicator.Margin;
        margin.Left = Math.Max(0, TabDropPosition.SlotOffset(drop.Widths, drop.Slot) - (TabDropIndicator.Width / 2));
        TabDropIndicator.Margin = margin;
        TabDropIndicator.Visibility = Visibility.Visible;
    }

    private void TabStrip_DragLeave(object sender, DragEventArgs e)
        => TabDropIndicator.Visibility = Visibility.Collapsed;

    private void TabStrip_Drop(object sender, DragEventArgs e)
    {
        TabDropIndicator.Visibility = Visibility.Collapsed;
        e.Handled = true;
        if (TryGetTabDrop(e) is { } drop) TabsViewModel!.MoveTab(drop.Tab, drop.Slot);
    }

    private readonly record struct TabDrop(OpenTab Tab, int Slot, IReadOnlyList<double> Widths);

    // Tab đang kéo cùng khe thả dưới con trỏ; null khi dữ liệu kéo không phải một Tab kéo được của strip này.
    private TabDrop? TryGetTabDrop(DragEventArgs e)
    {
        if (e.Data.GetData(typeof(OpenTab)) is not OpenTab tab) return null;
        if (TabsViewModel is not { } viewModel || !viewModel.CanDragTab(tab)) return null;

        var widths = new List<double>(TabStripItems.Items.Count);
        for (int index = 0; index < TabStripItems.Items.Count; index++)
            widths.Add((TabStripItems.ItemContainerGenerator.ContainerFromIndex(index) as FrameworkElement)?.ActualWidth ?? 0);

        int slot = TabDropPosition.SlotAt(widths, e.GetPosition(TabStripItems).X, viewModel.FirstDropSlot);
        return new TabDrop(tab, slot, widths);
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
