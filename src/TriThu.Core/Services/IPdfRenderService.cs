using SkiaSharp;

namespace PdfReaderApp.Services;

public interface IPdfRenderService : IDisposable
{
    void LoadDocument(byte[] data);
    int PageCount { get; }
    (double Width, double Height) GetPageSize(int pageIndex);
    SKBitmap RenderPage(int pageIndex, float scale, int dpi = 96);
    int GetCharCount(int pageIndex);
    string GetText(int pageIndex, int startChar, int count);
    IReadOnlyList<SKRect> GetTextBounds(int pageIndex, int startChar, int count);
}
