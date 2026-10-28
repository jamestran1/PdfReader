using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

internal sealed class FakeSessionSearch(CallLog log) : IReadingSessionSearch
{
    public void Reset() => log.Add("search.reset");

    public void ClearResults() => log.Add("search.clear");
}
