namespace PdfReaderApp.Core;

/// <summary>Điểm neo khi đổi zoom: chỉ số slot và tỉ lệ (0..1) theo chiều cao slot.</summary>
public sealed record SlotAnchor(int SlotIndex, double Fraction);
