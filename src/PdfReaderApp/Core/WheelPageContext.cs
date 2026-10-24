namespace PdfReaderApp.Core;

/// <summary>Trạng thái cuộn của viewer tại thời điểm lăn chuột (đầu vào cho ResolvePageChange).</summary>
public sealed record WheelPageContext(
    int Delta, bool AtTop, bool AtBottom, int CurrentPage, int TotalPages, bool IsFacing, bool ShowCover);
