namespace PdfReaderApp.Platform;

public interface IUiDispatcher
{
    void Post(Action action);
    IDisposable CreateTimer(TimeSpan interval, Action tick);
}
