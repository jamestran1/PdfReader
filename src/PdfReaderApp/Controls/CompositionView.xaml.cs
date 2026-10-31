using System.Windows.Controls;
using System.Windows.Threading;
using PdfReaderApp.Core;
using PdfReaderApp.Models;

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
}
