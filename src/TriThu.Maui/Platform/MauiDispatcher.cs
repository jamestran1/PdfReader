using PdfReaderApp.Platform;

namespace TriThu.Maui.Platform;

public sealed class MauiDispatcher : IUiDispatcher
{
    public void Post(Action action) => MainThread.BeginInvokeOnMainThread(action);

    public IDisposable CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = Application.Current!.Dispatcher.CreateTimer();
        timer.Interval = interval;
        timer.IsRepeating = true;
        timer.Tick += (_, _) => tick();
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle(IDispatcherTimer timer) : IDisposable
    {
        public void Dispose() => timer.Stop();
    }
}
