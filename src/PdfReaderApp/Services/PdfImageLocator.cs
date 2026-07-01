using System.Collections.Generic;
using iText.Kernel.Geom;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfReaderApp.Core;

namespace PdfReaderApp.Services;

public static class PdfImageLocator
{
    public readonly record struct NormalizedRect(double Left, double Top, double Width, double Height);

    public static Dictionary<int, IReadOnlyList<NormalizedRect>> GetNormalizedImageRectsByPage(string path)
    {
        var result = new Dictionary<int, IReadOnlyList<NormalizedRect>>();
        using var reader = new PdfReader(path);
        using var document = new PdfDocument(reader);
        for (int pageNumber = 1; pageNumber <= document.GetNumberOfPages(); pageNumber++)
        {
            var page = document.GetPage(pageNumber);
            iText.Kernel.Geom.Rectangle pageSize = page.GetPageSize();
            var listener = new ImageRectListener(pageSize.GetWidth(), pageSize.GetHeight());
            new PdfCanvasProcessor(listener).ProcessPageContent(page);
            result[pageNumber - 1] = listener.Rects;
        }
        return result;
    }

    private sealed class ImageRectListener : IEventListener
    {
        private readonly double _pageWidthPt;
        private readonly double _pageHeightPt;
        public readonly List<NormalizedRect> Rects = new();

        public ImageRectListener(double pageWidthPt, double pageHeightPt)
        {
            _pageWidthPt = pageWidthPt;
            _pageHeightPt = pageHeightPt;
        }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_IMAGE || data is not ImageRenderInfo info) return;
            Matrix ctm = info.GetImageCtm();

            // Ảnh chiếm hình vuông đơn vị [0,1]x[0,1] biến đổi qua CTM.
            // iText 8 Matrix dùng 6 phần tử: [a b 0 / c d 0 / e f 1] (row-major).
            // Chỉ số: 0=a,1=b, 3=c,4=d, 6=e,7=f (theo Matrix.I11..I33).
            // Biến đổi điểm (x,y): x' = a*x + c*y + e, y' = b*x + d*y + f
            float a = ctm.Get(Matrix.I11);
            float b = ctm.Get(Matrix.I12);
            float c = ctm.Get(Matrix.I21);
            float d = ctm.Get(Matrix.I22);
            float e = ctm.Get(Matrix.I31);
            float f = ctm.Get(Matrix.I32);

            // Chỉ giữ nguyên ảnh axis-aligned. Với ảnh xoay/nghiêng (b,c != 0), bounding box
            // trục-toạ-độ bao rộng hơn ảnh nên vẽ lại patch sẽ tạo viền chữ quanh ảnh → bỏ qua.
            if (!PdfImageRegion.IsAxisAligned(b, c)) return;

            (float px, float py)[] corners =
            {
                TransformPoint(0, 0, a, b, c, d, e, f),
                TransformPoint(1, 0, a, b, c, d, e, f),
                TransformPoint(0, 1, a, b, c, d, e, f),
                TransformPoint(1, 1, a, b, c, d, e, f),
            };

            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var (px, py) in corners)
            {
                if (px < minX) minX = px; if (px > maxX) maxX = px;
                if (py < minY) minY = py; if (py > maxY) maxY = py;
            }

            var (left, top, width, height) = PdfImageRegion.Normalize(
                minX, minY, maxX - minX, maxY - minY, _pageWidthPt, _pageHeightPt);
            Rects.Add(new NormalizedRect(left, top, width, height));
        }

        private static (float, float) TransformPoint(float x, float y, float a, float b, float c, float d, float e, float f)
            => (a * x + c * y + e, b * x + d * y + f);

        public ICollection<EventType> GetSupportedEvents() => null!; // null = tất cả sự kiện
    }
}
