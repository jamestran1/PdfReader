namespace PdfReaderApp.Core;

/// <summary>Điểm neo (toạ độ chuột trong ScrollViewer) cùng offset cuộn hiện tại khi zoom bằng Ctrl+lăn.</summary>
public sealed record WheelZoomAnchor(double HorizontalOffset, double VerticalOffset, double MouseX, double MouseY);
