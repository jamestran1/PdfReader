using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

internal sealed class FakeSessionIndexing(CallLog log) : IReadingSessionIndexing
{
    public void StartBackgroundIndexing() => log.Add("indexing.start");
}
