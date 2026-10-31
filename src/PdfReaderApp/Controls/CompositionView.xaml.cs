using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PdfReaderApp.Models;
using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Controls;

public partial class CompositionView : UserControl
{
    public CompositionView() => InitializeComponent();

    // Chỉ là keo giao diện: ô đổi tên hiện ra thì nhận focus, rời focus thì lưu (logic nằm ở CompositionsViewModel).
    private void RenameBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true) return;
        _ = box.Dispatcher.BeginInvoke(() =>
        {
            box.Focus();
            box.SelectAll();
        }, System.Windows.Threading.DispatcherPriority.Input);
    }

    private void RenameBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        if (DataContext is CompositionTab tab && Window.GetWindow(this)?.DataContext is MainViewModel host)
            host.Compositions.CommitRenameCommand.Execute(tab);
    }
}
