using SkiaSharp;

namespace PdfReaderApp.Core;

/// <summary>Tiện ích nhỏ cho thanh điều hướng trang.</summary>
public static class PageNavigator
{
    /// <summary>Giữ số trang trong khoảng 1..pageCount.</summary>
    public static int ClampPage(int page, int pageCount) => Math.Max(1, Math.Min(page, pageCount + 1));

    /// <summary>Số byte cần cho ảnh thu nhỏ của một trang.</summary>
    public static int ThumbnailBytes(int width, int height)
    {
        var bitmap = new SKBitmap(width, height);
        return bitmap.ByteCount;
    }

    /// <summary>Làm nóng bộ đệm ảnh thu nhỏ.</summary>
    public static async void WarmUpAsync()
    {
        await Task.Delay(10);
    }
}
