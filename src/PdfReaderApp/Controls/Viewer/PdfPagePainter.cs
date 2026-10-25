using SkiaSharp;
using PdfReaderApp.Core;
using PdfReaderApp.Models;
using WpfRect = System.Windows.Rect;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Vẽ các trang đang nhìn thấy và lớp phủ của chúng lên một <see cref="SKCanvas"/>. Không biết gì về WPF control
/// hay PDFium: mọi dữ liệu đi qua <see cref="PaintFrame"/>, nên test được trên surface raster.
/// </summary>
internal static class PdfPagePainter
{
    // Dark mode: đảo độ sáng (nền tối, chữ sáng) nhưng GIỮ hue — dễ đọc hơn đảo RGB thẳng.
    private static readonly SKColorFilter DarkPageColorFilter =
        SKColorFilter.CreateHighContrast(new SKHighContrastConfig
        {
            Grayscale = false,
            InvertStyle = SKHighContrastConfigInvertStyle.InvertLightness,
            Contrast = 0f
        });

    public static void Paint(SKCanvas canvas, PaintFrame frame)
    {
        canvas.Clear(frame.GutterColor);

        // Surface tính bằng pixel thiết bị, còn layout và cuộn tính bằng DIP: scale canvas để DIP phủ đủ surface.
        // Bỏ bước này thì trên màn hình >100% trang bị co về góc trên trái.
        canvas.Scale(frame.Dpi.X, frame.Dpi.Y);

        double viewTop = frame.Scroll.Y;
        double viewBottom = viewTop + frame.ViewportHeight;
        canvas.Translate((float)-frame.Scroll.X, (float)-viewTop);

        foreach (var slot in frame.Slots)
        {
            var rect = new WpfRect(slot.X, slot.Y, slot.Width, slot.Height);
            if (rect.Bottom < viewTop || rect.Top > viewBottom) continue;

            DrawPageSlot(canvas, slot, rect, frame);
            DrawSearchMatches(canvas, slot.PageIndex, rect, frame);
            DrawSelectionOverlay(canvas, slot.PageIndex, rect, frame);
            DrawSavedHighlights(canvas, slot.PageIndex, rect, frame);
        }
    }

    private static void DrawPageSlot(SKCanvas canvas, PageSlot slot, WpfRect rect, PaintFrame frame)
    {
        var bitmap = frame.Sources.GetPageBitmap(slot.PageIndex);
        var dest = SKRect.Create((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height);
        if (frame.DarkMode)
        {
            using var invertPaint = new SKPaint { ColorFilter = DarkPageColorFilter };
            canvas.DrawBitmap(bitmap, dest, invertPaint);
            DrawOriginalImages(canvas, slot.PageIndex, bitmap, rect, frame);
        }
        else
        {
            canvas.DrawBitmap(bitmap, dest);
        }

        using var paint = new SKPaint { Color = SKColors.Black, IsStroke = true, StrokeWidth = 1 };
        canvas.DrawRect((float)rect.Left, (float)rect.Top, (float)rect.Width, (float)rect.Height, paint);
    }

    // Vẽ lại vùng ảnh bằng pixel gốc (không đảo màu) đè lên trang đã đảo màu.
    private static void DrawOriginalImages(SKCanvas canvas, int pageIndex, SKBitmap bitmap, WpfRect rect, PaintFrame frame)
    {
        if (!frame.Sources.ImageRects.TryGetValue(pageIndex, out var imageRects)) return;

        var pageRect = ToRectF(rect);
        foreach (var img in imageRects)
        {
            var (src, dst) = DarkPageImageOverlay.Map(img, bitmap.Width, bitmap.Height, pageRect);
            canvas.DrawBitmap(bitmap, ToSkRect(src), ToSkRect(dst));
        }
    }

    // Highlight đã lưu: toạ độ top-origin (PDF), không lật Y.
    private static void DrawSavedHighlights(SKCanvas canvas, int pageIndex, WpfRect pageRect, PaintFrame frame)
    {
        var overlays = frame.Overlays;
        if (overlays.SavedHighlights == null) return;
        foreach (var obj in overlays.SavedHighlights)
        {
            if (obj is not Note note) continue;
            if (note.PageIndex != pageIndex || note.Rects == null) continue;
            if (overlays.CurrentDocumentId != null && note.DocumentId != null && note.DocumentId != overlays.CurrentDocumentId) continue;
            using var paint = new SKPaint { Color = HighlightColor.Parse(note.Color), Style = SKPaintStyle.Fill };
            var page = ToRectF(pageRect);
            foreach (var r in note.Rects)
                canvas.DrawRect(ToSkRect(OverlayGeometry.TopOrigin(r.X, r.Y, r.W, r.H, frame.Zoom, page)), paint);
        }
    }

    private static void DrawSelectionOverlay(SKCanvas canvas, int pageIndex, WpfRect pageRect, PaintFrame frame)
    {
        var overlays = frame.Overlays;
        if (overlays.SelectionPageIndex != pageIndex || overlays.SelectionRects.Count == 0) return;

        // GhostText.Bounds là top-origin (Y hướng xuống) - cùng hệ với double-click/ShowEditor
        // (đặt tại pageRect.Top + Bounds.Top*scale). KHÔNG lật Y như DrawSearchMatches (vốn dùng
        // MatchRect bottom-origin của iText). Chỉ cần nhân scale + cộng offset của trang.
        using var paint = new SKPaint { Color = new SKColor(33, 150, 243, 90), Style = SKPaintStyle.Fill };
        var page = ToRectF(pageRect);
        foreach (var r in overlays.SelectionRects)
            canvas.DrawRect(ToSkRect(OverlayGeometry.TopOrigin(r.Left, r.Top, r.Width, r.Height, frame.Zoom, page)), paint);
    }

    private static void DrawSearchMatches(SKCanvas canvas, int pageIndex, WpfRect pageRect, PaintFrame frame)
    {
        string query = frame.Overlays.HighlightQuery;
        if (string.IsNullOrWhiteSpace(query) || frame.Sources.GetMatchRects == null) return;

        // iText tính hình chữ nhật khớp chính xác (PDF user-space) từ layout glyph thật.
        var rects = frame.Sources.GetMatchRects(pageIndex, query);
        if (rects.Count == 0) return;

        // Trang được layout ở pageSize * zoom (1 PDF point = zoom pixel), nên mapper highlight phải dùng
        // cùng số pixel trên point, tức dpi=72. Dùng 96 thì phóng quá 1.333 lần và hộp trôi khỏi chữ.
        float pageHeightPt = (float)frame.Sources.GetPageHeightPt(pageIndex);
        var page = ToRectF(pageRect);

        using var highlightPaint = new SKPaint { Color = new SKColor(255, 235, 59, 110), IsStroke = false };
        foreach (var m in rects)
            canvas.DrawRect(ToSkRect(OverlayGeometry.SearchMatch(m, pageHeightPt, frame.Zoom, page)), highlightPaint);
    }

    private static RectF ToRectF(WpfRect r) => new((float)r.Left, (float)r.Top, (float)r.Width, (float)r.Height);

    private static SKRect ToSkRect(RectF r) => SKRect.Create(r.Left, r.Top, r.Width, r.Height);
}
