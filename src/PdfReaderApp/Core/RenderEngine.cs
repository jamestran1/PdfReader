using System;
using PdfReaderApp.Services;
using SkiaSharp;

namespace PdfReaderApp.Core;

public class RenderEngine : IDisposable
{
    private readonly IPdfRenderService _pdfService;
    private bool _disposed;

    public RenderEngine(IPdfRenderService pdfService)
    {
        _pdfService = pdfService;
    }

    public SKBitmap RenderPage(int pageIndex, float scale, int dpi = 96)
    {
        return _pdfService.RenderPage(pageIndex, scale, dpi);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _disposed = true;
        }
    }
}
