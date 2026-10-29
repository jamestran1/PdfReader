using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

internal sealed class FakeSessionChat(CallLog log) : IReadingSessionChat
{
    public void CancelPendingRequest() => log.Add("chat.cancel");

    public void LoadHistory() => log.Add("chat.load");
}
