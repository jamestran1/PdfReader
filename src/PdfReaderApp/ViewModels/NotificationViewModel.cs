using CommunityToolkit.Mvvm.ComponentModel;

namespace PdfReaderApp.ViewModels;

/// <summary>Thông báo snackbar non-blocking, đáy-giữa, tự ẩn. Một surface duy nhất cho cả app (#67).</summary>
public sealed partial class NotificationViewModel : ObservableObject
{
    [ObservableProperty]
    public partial Notification? Current { get; set; }

    private System.Windows.Threading.DispatcherTimer? _timer;

    public void ShowSuccess(string message) => Show(new Notification(message, IsError: false));

    public void ShowError(string message) => Show(new Notification(message, IsError: true));

    private void Show(Notification notification)
    {
        Current = notification;
        // Unit test (không có Application): bỏ qua timer tự ẩn; chỉ cần Current được set.
        if (System.Windows.Application.Current is null) return;
        _timer ??= CreateTimer();
        _timer.Stop();
        _timer.Start();
    }

    private System.Windows.Threading.DispatcherTimer CreateTimer()
    {
        var timer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = System.TimeSpan.FromMilliseconds(2200)
        };
        timer.Tick += (_, _) => { _timer!.Stop(); Current = null; };
        return timer;
    }
}
