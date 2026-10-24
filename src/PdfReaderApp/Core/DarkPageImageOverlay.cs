namespace PdfReaderApp.Core;

/// <summary>
/// Logic thuần map vùng ảnh chuẩn hoá [0,1] lên bitmap trang (nguồn) và vùng vẽ trên canvas (đích),
/// dùng khi vẽ lại ảnh bằng pixel gốc đè lên trang đã đảo màu ở chế độ tối.
/// </summary>
public static class DarkPageImageOverlay
{
    public static (RectF Source, RectF Destination) Map(
        Services.PdfImageLocator.NormalizedRect image, int bitmapWidth, int bitmapHeight, RectF pageRect)
    {
        var source = new RectF(
            (float)(image.Left * bitmapWidth), (float)(image.Top * bitmapHeight),
            (float)(image.Width * bitmapWidth), (float)(image.Height * bitmapHeight));
        var destination = new RectF(
            (float)(pageRect.Left + image.Left * pageRect.Width), (float)(pageRect.Top + image.Top * pageRect.Height),
            (float)(image.Width * pageRect.Width), (float)(image.Height * pageRect.Height));
        return (source, destination);
    }
}
