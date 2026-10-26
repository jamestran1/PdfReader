using System.Windows;
using SkiaSharp;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>Dữ liệu các lớp phủ vẽ lên trang.</summary>
/// <param name="SelectionPageIndex">Trang đang có vùng chọn, -1 nếu không có.</param>
/// <param name="SelectionRects">Vùng chọn theo PDF points (top-origin) của trang đó.</param>
/// <param name="HighlightQuery">Từ khoá tìm kiếm đang tô.</param>
/// <param name="SavedHighlights">Ghi chú đã lưu (các <c>Note</c>).</param>
/// <param name="CurrentDocumentId">Chỉ vẽ ghi chú của tài liệu này (null thì vẽ hết).</param>
/// <param name="SourcePassageColor">Màu tô Đoạn nguồn, lấy từ token <c>TriThu.Brush.SourcePassage</c>.</param>
internal sealed record OverlayData(
    int SelectionPageIndex,
    IReadOnlyList<Rect> SelectionRects,
    string HighlightQuery,
    System.Collections.IEnumerable? SavedHighlights,
    string? CurrentDocumentId,
    SKColor SourcePassageColor = default)
{
    /// <summary>Cam nhạt, khác vàng của kết quả tìm và xanh của vùng chọn; dùng khi theme thiếu token.</summary>
    public static readonly SKColor DefaultSourcePassageColor = new(255, 152, 0, 80);
}
