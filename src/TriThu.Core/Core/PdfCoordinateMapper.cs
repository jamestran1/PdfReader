namespace PdfReaderApp.Core;

public sealed class PdfCoordinateMapper
{
    private readonly float _pageHeightPt;
    private readonly float _pixelsPerPoint;

    public PdfCoordinateMapper(float pageHeightPt, float scale, int dpi)
    {
        _pageHeightPt = pageHeightPt;
        _pixelsPerPoint = scale * (dpi / 72f);
    }

    // PDF user-space (bottom-left origin, points) -> render-space (top-left origin, pixels)
    public (float x, float y) PdfPointToRender(float pdfX, float pdfY)
    {
        float renderX = pdfX * _pixelsPerPoint;
        float renderY = (_pageHeightPt - pdfY) * _pixelsPerPoint;
        return (renderX, renderY);
    }

    // Render-space (top-left origin, pixels) -> PDF user-space (bottom-left origin, points)
    public (float x, float y) RenderPointToPdf(float renderX, float renderY)
    {
        float pdfX = renderX / _pixelsPerPoint;
        float pdfY = _pageHeightPt - renderY / _pixelsPerPoint;
        return (pdfX, pdfY);
    }
}
