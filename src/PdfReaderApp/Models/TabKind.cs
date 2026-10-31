namespace PdfReaderApp.Models;

/// <summary>Loại Tab trong Open Set (ADR 0009). Tab Document giữ PDFium, Tab Bản soạn hiển thị Markdown.</summary>
public enum TabKind
{
    Document,
    Composition,
}
