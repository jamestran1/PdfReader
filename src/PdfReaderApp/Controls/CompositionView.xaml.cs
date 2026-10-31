using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Controls;

public partial class CompositionView : UserControl
{
    // Đặt offset từ ScrollNorm đã lưu; trong lúc đó ScrollChanged không được ghi ngược (offset tạm có thể là 0).
    private bool _restoringScroll;

    public CompositionView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => RestoreScroll();
        Scroller.ScrollChanged += OnScrollChanged;
    }

    private void RestoreScroll()
    {
        if (DataContext is not CompositionTab tab) return;
        _restoringScroll = true;
        // Sau layout: ScrollableHeight chỉ đúng khi nội dung mới đã đo xong.
        _ = Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            Scroller.ScrollToVerticalOffset(CompositionScroll.ToOffset(tab.ScrollNorm, Scroller.ScrollableHeight));
            _restoringScroll = false;
        });
    }

    private void OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_restoringScroll || e.VerticalChange == 0 || DataContext is not CompositionTab tab) return;
        tab.ScrollNorm = CompositionScroll.ToNormalized(Scroller.VerticalOffset, Scroller.ScrollableHeight);
    }

    // Chỉ là keo giao diện: ô đổi tên hiện ra thì nhận focus, rời focus thì lưu (logic nằm ở CompositionsViewModel).
    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true) return;
        _ = box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Input);
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is CompositionTab tab && Window.GetWindow(this)?.DataContext is MainViewModel host)
            host.Compositions.CommitRenameCommand.Execute(tab);
    }
}
