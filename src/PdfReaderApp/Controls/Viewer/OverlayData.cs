using System.Windows;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>Dữ liệu các lớp phủ vẽ lên trang.</summary>
/// <param name="SelectionPageIndex">Trang đang có vùng chọn, -1 nếu không có.</param>
/// <param name="SelectionRects">Vùng chọn theo PDF points (top-origin) của trang đó.</param>
/// <param name="HighlightQuery">Từ khoá tìm kiếm đang tô.</param>
/// <param name="SavedHighlights">Ghi chú đã lưu (các <c>Note</c>).</param>
/// <param name="CurrentDocumentId">Chỉ vẽ ghi chú của tài liệu này (null thì vẽ hết).</param>
internal sealed record OverlayData(
    int SelectionPageIndex,
    IReadOnlyList<Rect> SelectionRects,
    string HighlightQuery,
    System.Collections.IEnumerable? SavedHighlights,
    string? CurrentDocumentId);
