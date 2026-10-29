using PdfReaderApp.ViewModels;

namespace PdfReaderApp.Tests.Fakes;

internal sealed class FakeSessionSurface(CallLog log) : IReadingSessionSurface
{
    public void Refresh() => log.Add("surface.refresh");

    public void DismissModal() => log.Add("surface.dismissModal");
}
