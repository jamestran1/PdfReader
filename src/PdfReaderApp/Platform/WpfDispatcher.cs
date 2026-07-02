using System;
using System.Windows.Threading;
using PdfReaderApp.Platform;

namespace PdfReaderApp.Wpf.Platform;

public sealed class WpfDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher;

    public WpfDispatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void Post(Action action) => _dispatcher.BeginInvoke(action);

    public IDisposable CreateTimer(TimeSpan interval, Action tick)
    {
        var timer = new DispatcherTimer(interval, DispatcherPriority.Background, (_, _) => tick(), _dispatcher);
        timer.Start();
        return new TimerHandle(timer);
    }

    private sealed class TimerHandle : IDisposable
    {
        private readonly DispatcherTimer _timer;
        public TimerHandle(DispatcherTimer timer) => _timer = timer;
        public void Dispose() => _timer.Stop();
    }
}
