using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>
/// Logic thuần đổi rect overlay (kết quả tìm, vùng chọn, note) từ toạ độ PDF sang toạ độ canvas.
/// </summary>
public static class OverlayGeometry
{
    // Trang được dàn trên canvas ở pageSize * zoom, tức 1 point = zoom pixel. Mapper phải dùng đúng
    // tỉ lệ đó nên dpi = 72; dùng 96 sẽ phóng quá 1.333 lần và rect trôi khỏi chữ.
    private const int LayoutDpi = 72;

    // MatchRect (PdfX, PdfY) là góc dưới-trái, trục Y của PDF hướng lên: cạnh trên là PdfY + Height.
    public static RectF BottomOrigin(MatchRect match, float pageHeightPt, float zoom, RectF pageRect)
    {
        var mapper = new PdfCoordinateMapper(pageHeightPt, zoom, LayoutDpi);
        var (x, y) = mapper.PdfPointToRender(match.PdfX, match.PdfY + match.Height);
        return new RectF(pageRect.Left + x, pageRect.Top + y, match.Width * zoom, match.Height * zoom);
    }

    // Cuộn tới đoạn nguồn thay vì đầu trang: đưa hộp trên cùng lên khoảng một phần ba vùng nhìn để còn thấy
    // chữ phía trước. Không cuộn lên trên đầu trang, nên đoạn nằm sát đỉnh trang vẫn thấy cả đỉnh trang.
    public static double PassageScrollOffset(
        double slotY, float pageHeightPt, float zoom, IReadOnlyList<MatchRect> rects, double viewportHeight)
    {
        if (rects.Count == 0) return slotY;

        double top = slotY + (pageHeightPt - rects.Max(r => r.PdfY + r.Height)) * zoom;
        return Math.Max(slotY, top - viewportHeight / 3);
    }

    // Vùng chọn và note đã ở gốc trên-trái (Y hướng xuống): chỉ nhân zoom rồi cộng offset trang, không lật Y.
    public static RectF TopOrigin(double x, double y, double width, double height, float zoom, RectF pageRect)
        => new(
            (float)(pageRect.Left + x * zoom),
            (float)(pageRect.Top + y * zoom),
            (float)(width * zoom),
            (float)(height * zoom));
}
