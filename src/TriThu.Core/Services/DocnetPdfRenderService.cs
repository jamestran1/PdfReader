using System.Runtime.InteropServices;
using Docnet.Core;
using Docnet.Core.Converters;
using Docnet.Core.Models;
using Docnet.Core.Readers;
using SkiaSharp;

namespace PdfReaderApp.Services;

public sealed class DocnetPdfRenderService : IPdfRenderService
{
    private byte[]? _docBytes;
    private IDocReader? _metaReader;
    private double _metaScale = 1.0;

    public void LoadDocument(byte[] data)
    {
        _metaReader?.Dispose();
        _docBytes = data;
        _metaReader = DocLib.Instance.GetDocReader(data, new PageDimensions(1.0));
        _metaScale = 1.0;
    }

    public int PageCount => _metaReader?.GetPageCount() ?? 0;

    public (double Width, double Height) GetPageSize(int pageIndex)
    {
        if (_metaReader is null) return (0, 0);
        using var page = _metaReader.GetPageReader(pageIndex);
        return (page.GetPageWidth() / _metaScale, page.GetPageHeight() / _metaScale);
    }

    public SKBitmap RenderPage(int pageIndex, float scale, int dpi = 96)
    {
        if (_docBytes is null) return new SKBitmap(1, 1);

        double ppp = dpi / 72.0 * scale;
        using var reader = DocLib.Instance.GetDocReader(_docBytes, new PageDimensions(ppp));
        using var page = reader.GetPageReader(pageIndex);

        int width = page.GetPageWidth();
        int height = page.GetPageHeight();
        byte[] bgra = page.GetImage(new NaiveTransparencyRemover(), RenderFlags.RenderAnnotations);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var bitmap = new SKBitmap(info);

        var handle = GCHandle.Alloc(bgra, GCHandleType.Pinned);
        try
        {
            bitmap.InstallPixels(info, handle.AddrOfPinnedObject(), info.RowBytes);
            return bitmap.Copy();
        }
        finally
        {
            handle.Free();
        }
    }

    public int GetCharCount(int pageIndex)
    {
        if (_metaReader is null) return 0;
        using var page = _metaReader.GetPageReader(pageIndex);
        return page.GetCharacters().Count();
    }

    public string GetText(int pageIndex, int startChar, int count)
    {
        if (_metaReader is null) return string.Empty;
        using var page = _metaReader.GetPageReader(pageIndex);
        var chars = page.GetCharacters().Skip(startChar).Take(count);
        return new string(chars.Select(c => c.Char).ToArray());
    }

    public IReadOnlyList<SKRect> GetTextBounds(int pageIndex, int startChar, int count)
    {
        if (_metaReader is null) return Array.Empty<SKRect>();
        using var page = _metaReader.GetPageReader(pageIndex);

        int pageW = page.GetPageWidth();
        int pageH = page.GetPageHeight();
        var (origW, origH) = GetPageSize(pageIndex);

        var rects = new List<SKRect>(count);
        foreach (var ch in page.GetCharacters().Skip(startChar).Take(count))
        {
            float left = (float)(ch.Box.Left / _metaScale);
            float top = (float)(ch.Box.Top / _metaScale);
            float right = (float)(ch.Box.Right / _metaScale);
            float bottom = (float)(ch.Box.Bottom / _metaScale);
            rects.Add(new SKRect(left, top, right, bottom));
        }
        return rects;
    }

    public void Dispose()
    {
        _metaReader?.Dispose();
        _metaReader = null;
        _docBytes = null;
    }
}

internal sealed class NaiveTransparencyRemover : IImageBytesConverter
{
    public void Convert(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i += 4)
        {
            bytes[i + 3] = 255;
        }
    }
}
