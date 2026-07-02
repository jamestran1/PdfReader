namespace PdfReaderApp.Models;

public sealed record TextBlock(
    string Text,
    float PdfX,
    float PdfY,
    float Width,
    float Height,
    float FontSize,
    int PageIndex,
    string StructureType);
