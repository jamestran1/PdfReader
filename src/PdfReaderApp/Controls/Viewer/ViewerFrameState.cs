using System.Windows;
using PdfReaderApp.Core;
using PdfReaderApp.Services;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Ảnh chụp trạng thái của control cho một lần vẽ, do control dựng từ DependencyProperty và scroll viewer.
/// Tách khỏi control để <see cref="ViewerFrameComposer"/> dựng khung vẽ mà không cần WPF visual tree.
/// </summary>
/// <param name="Slots">Vị trí các trang trên canvas (DIP).</param>
/// <param name="Zoom">Số pixel trên một PDF point.</param>
/// <param name="CanvasSize">Kích thước canvas theo DIP, để suy ra hệ số DPI so với surface.</param>
/// <param name="Scroll">Offset cuộn ngang và dọc (DIP).</param>
/// <param name="ViewportHeight">Chiều cao vùng nhìn (DIP).</param>
/// <param name="GutterResource">Tài nguyên nền quanh trang tìm được trong theme; không phải brush đặc thì dùng màu mặc định.</param>
/// <param name="SelectionPageIndex">Trang đang có vùng chọn, -1 nếu không có.</param>
/// <param name="SelectionRects">Vùng chọn theo PDF points của trang đó.</param>
/// <param name="HighlightQuery">Từ khoá tìm kiếm đang tô.</param>
/// <param name="SavedHighlights">Ghi chú đã lưu.</param>
/// <param name="CurrentDocumentId">Chỉ vẽ ghi chú của tài liệu này (null thì vẽ hết).</param>
/// <param name="MatchSource">Nguồn tìm vị trí từ khoá; null thì không tô kết quả tìm.</param>
/// <param name="SourceHighlight">Đoạn nguồn của trích dẫn AI đang mở; chỉ tô khi thuộc <paramref name="CurrentDocumentId"/>.</param>
internal sealed record ViewerFrameState(
    IReadOnlyList<PageSlot> Slots,
    double Zoom,
    (double Width, double Height) CanvasSize,
    (double X, double Y) Scroll,
    double ViewportHeight,
    object? GutterResource,
    int SelectionPageIndex,
    IReadOnlyList<Rect> SelectionRects,
    string HighlightQuery,
    System.Collections.IEnumerable? SavedHighlights,
    string? CurrentDocumentId,
    IPdfDocumentService? MatchSource,
    SourceHighlight? SourceHighlight = null);
