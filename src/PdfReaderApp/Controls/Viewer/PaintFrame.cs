using PdfReaderApp.Core;
using SkiaSharp;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Ảnh chụp trạng thái cần cho một lần vẽ, do control dựng từ DependencyProperty và state hiện tại.
/// </summary>
/// <param name="Slots">Vị trí các trang trên canvas (DIP).</param>
/// <param name="Zoom">Số pixel trên một PDF point.</param>
/// <param name="Dpi">Hệ số DPI ngang và dọc của surface so với DIP.</param>
/// <param name="Scroll">Offset cuộn ngang và dọc (DIP).</param>
/// <param name="ViewportHeight">Chiều cao vùng nhìn (DIP), để bỏ qua trang ngoài khung.</param>
/// <param name="GutterColor">Màu nền quanh các trang.</param>
/// <param name="DarkMode">Đảo độ sáng trang, giữ nguyên vùng ảnh.</param>
/// <param name="Overlays">Dữ liệu các lớp phủ (chọn text, tìm kiếm, ghi chú).</param>
/// <param name="Sources">Nguồn bitmap trang, vùng ảnh và kích thước trang.</param>
internal sealed record PaintFrame(
    IReadOnlyList<PageSlot> Slots,
    float Zoom,
    (float X, float Y) Dpi,
    (double X, double Y) Scroll,
    double ViewportHeight,
    SKColor GutterColor,
    bool DarkMode,
    OverlayData Overlays,
    PageSources Sources);
