using System.Drawing;
using SkiaSharp;
using PdfiumViewer; // For PdfRotation
using PdfiumViewer.Core;
using PdfiumViewer.Enums;

namespace PdfReaderApp.Core;

public class RenderEngine : IDisposable
{
    private bool _disposed;

#pragma warning disable CA1822 // API công khai theo thể hiện: đổi sang static làm vỡ chỗ gọi qua _renderEngine và seam thay thế engine
    public SKBitmap RenderPage(PdfPage page, float scale, int dpi = 96)
    {
        int width = (int)(page.Width * scale);
        int height = (int)(page.Height * scale);

        // Render PDF page to GDI+ Bitmap
        using var image = (Bitmap)page.Render(width, height, dpi, dpi, PdfRotation.Rotate0, PdfRenderFlags.Annotations);

        // Convert GDI+ Bitmap to SkiaSharp SKBitmap
        return ToSKBitmap(image);
    }
#pragma warning restore CA1822

    private static SKBitmap ToSKBitmap(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                                   System.Drawing.Imaging.ImageLockMode.ReadOnly,
                                   System.Drawing.Imaging.PixelFormat.Format32bppPArgb);

        var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var skBitmap = new SKBitmap();
        skBitmap.InstallPixels(info, data.Scan0, data.Stride);

        // We need to copy the pixels because the GDI bitmap will be disposed
        var result = skBitmap.Copy();

        bitmap.UnlockBits(data);
        skBitmap.Dispose();

        return result;
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}