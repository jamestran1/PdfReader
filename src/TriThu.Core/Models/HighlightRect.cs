namespace PdfReaderApp.Models;

/// <summary>Một dải highlight trên trang, tọa độ PDF top-origin (Y hướng xuống).</summary>
public sealed record HighlightRect(double X, double Y, double W, double H);
