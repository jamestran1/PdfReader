using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

public sealed class ITextPdfDocumentService : IPdfDocumentService
{
    private PdfDocument? _pdfDoc;
    private PdfReader? _pdfReader;

    public void LoadFile(string filePath)
    {
        _pdfDoc?.Close();
        _pdfReader?.Close();

        _pdfReader = new PdfReader(filePath);
        _pdfDoc = new PdfDocument(_pdfReader);
    }

    public List<TextBlock> ExtractStructure()
    {
        if (_pdfDoc is null)
            throw new InvalidOperationException("Call LoadFile before ExtractStructure.");

        var blocks = new List<TextBlock>();

        for (int pageIndex = 0; pageIndex < _pdfDoc.GetNumberOfPages(); pageIndex++)
        {
            var page = _pdfDoc.GetPage(pageIndex + 1); // iText pages are 1-indexed
            var listener = new TextItemListener();
            var processor = new PdfCanvasProcessor(listener);
            processor.ProcessPageContent(page);

            foreach (var item in listener.Items)
            {
                blocks.Add(new TextBlock(
                    Text: item.Text,
                    PdfX: item.X,
                    PdfY: item.Y,
                    Width: item.Width,
                    Height: item.FontSize,
                    FontSize: item.FontSize,
                    PageIndex: pageIndex,
                    StructureType: ClassifyStructure(item.Text, item.FontSize)));
            }
        }

        return blocks;
    }

    private static string ClassifyStructure(string text, float fontSize)
    {
        if (fontSize >= 14f) return "Heading";
        if (text.TrimStart() is { } t && (t.StartsWith('•') || t.StartsWith('-'))) return "List";
        return "Paragraph";
    }

    public void Dispose()
    {
        _pdfDoc?.Close();
        _pdfReader?.Close();
        _pdfDoc = null;
        _pdfReader = null;
    }

    private sealed class TextItemListener : IEventListener
    {
        public List<RawTextItem> Items { get; } = new();

        public void EventOccurred(IEventData data, EventType type)
        {
            if (data is not TextRenderInfo renderInfo) return;

            string text = renderInfo.GetText();
            if (string.IsNullOrWhiteSpace(text)) return;

            var baseline = renderInfo.GetBaseline();
            var startPt = baseline.GetStartPoint();
            float x = startPt.Get(0); // index 0 = X in PDF user-space
            float y = startPt.Get(1); // index 1 = Y in PDF user-space
            float width = baseline.GetEndPoint().Get(0) - x;
            float fontSize = renderInfo.GetFontSize();

            Items.Add(new RawTextItem(text, x, y, width, fontSize));
        }

        public ICollection<EventType> GetSupportedEvents()
            => new HashSet<EventType> { EventType.RENDER_TEXT };
    }

    private sealed record RawTextItem(string Text, float X, float Y, float Width, float FontSize);
}
