namespace PdfReaderApp.Core;

// Chuẩn hoá rect của ảnh (toạ độ PDF, gốc dưới-trái, đơn vị point) về [0,1] gốc trên-trái,
// để map lên bitmap trang và vùng vẽ trên canvas mà không phụ thuộc đơn vị/kích thước cụ thể.
public static class PdfImageRegion
{
    public static (double Left, double Top, double Width, double Height) Normalize(
        double imageLeftPt, double imageBottomPt, double imageWidthPt, double imageHeightPt,
        double pageWidthPt, double pageHeightPt)
    {
        if (pageWidthPt <= 0 || pageHeightPt <= 0)
            return (0, 0, 0, 0);
        double left = imageLeftPt / pageWidthPt;
        double topFromTop = (pageHeightPt - (imageBottomPt + imageHeightPt)) / pageHeightPt;
        double width = imageWidthPt / pageWidthPt;
        double height = imageHeightPt / pageHeightPt;
        return (left, topFromTop, width, height);
    }
}
