namespace PdfReaderApp.Core;

/// <summary>Trang trúng và toạ độ điểm đó trong hệ PDF (đơn vị điểm, đã chia theo zoom).</summary>
public sealed record SlotHit(PageSlot Slot, double PdfX, double PdfY);
