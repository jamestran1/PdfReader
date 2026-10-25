namespace PdfReaderApp.Core;

/// <summary>
/// Ảnh chụp trạng thái cuộn dọc của ScrollViewer tại một tick.
/// </summary>
/// <param name="ScrollableHeight">Chiều cao cuộn được hiện tại.</param>
/// <param name="VerticalOffset">Offset dọc hiện tại.</param>
public sealed record ScrollViewport(double ScrollableHeight, double VerticalOffset);
