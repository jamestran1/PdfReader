namespace PdfReaderApp.Core;

/// <summary>
/// Kết quả một tick ổn định hoá: có dừng timer không, và có phải cuộn lại tới offset đích không.
/// </summary>
/// <param name="ShouldStop">true khi timer phải dừng.</param>
/// <param name="ReassertOffset">Offset dọc cần cuộn lại tới; null nếu không phải cuộn.</param>
public readonly record struct ScrollSettleStep(bool ShouldStop, double? ReassertOffset);
