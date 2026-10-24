namespace PdfReaderApp.Core;

public sealed record LayoutResult(
    IReadOnlyList<PageSlot> Slots,
    double ContentWidth,
    double ContentHeight,
    IReadOnlyList<int> UnitStartPages);
