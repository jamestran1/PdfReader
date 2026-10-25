using SkiaSharp;

namespace PdfReaderApp.Controls.Viewer;

/// <summary>
/// Giữ bitmap đã render theo chỉ số trang. Sở hữu các <see cref="SKBitmap"/> nên phải Dispose (CS-2).
/// </summary>
internal sealed class PageBitmapCache : IDisposable
{
    private readonly Dictionary<int, SKBitmap> _bitmaps = [];

    /// <summary>Trả bitmap đã có của trang, chưa có thì gọi <paramref name="render"/> và giữ lại.</summary>
    public SKBitmap GetOrRender(int pageIndex, Func<SKBitmap> render)
    {
        if (!_bitmaps.TryGetValue(pageIndex, out var bitmap))
        {
            bitmap = render();
            _bitmaps[pageIndex] = bitmap;
        }

        return bitmap;
    }

    /// <summary>Giải phóng và bỏ toàn bộ bitmap (đổi zoom, đổi chế độ xem hoặc đóng tài liệu).</summary>
    public void Clear()
    {
        foreach (var bitmap in _bitmaps.Values)
            bitmap.Dispose();
        _bitmaps.Clear();
    }

    public void Dispose() => Clear();
}
