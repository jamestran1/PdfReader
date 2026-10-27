using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;
using System.Text.RegularExpressions;
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
        _pdfDoc = null;
        _pdfReader = null;

        _pdfReader = new PdfReader(filePath);
        _pdfDoc = new PdfDocument(_pdfReader);
    }

    public IReadOnlyList<PageText> ExtractPageTexts()
    {
        if (_pdfDoc is null)
            throw new InvalidOperationException("Call LoadFile before ExtractPageTexts.");

        var result = new List<PageText>();
        int pageCount = _pdfDoc.GetNumberOfPages();
        for (int i = 0; i < pageCount; i++)
        {
            var page = _pdfDoc.GetPage(i + 1);
            string text = PdfTextExtractor.GetTextFromPage(page, new LocationTextExtractionStrategy());
            result.Add(new PageText(i, text));
        }
        return result;
    }

    public IReadOnlyList<TextBlock> ExtractStructure()
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

    public IReadOnlyList<MatchRect> FindMatchRects(int pageIndex, string query)
    {
        if (_pdfDoc is null)
            throw new InvalidOperationException("Call LoadFile before FindMatchRects.");

        return LocateRects(pageIndex, AccentInsensitiveRegex.BuildPattern(query));
    }

    public IReadOnlyList<MatchRect> FindPassageRects(int pageIndex, IReadOnlyList<string> fragments)
    {
        if (_pdfDoc is null)
            throw new InvalidOperationException("Call LoadFile before FindPassageRects.");

        // One alternation pattern: a single pass over the page content instead of one per fragment.
        return LocateRects(pageIndex, AccentInsensitiveRegex.BuildAlternation(fragments));
    }

    public PassageHits FindPassageHits(int pageIndex, IReadOnlyList<string> fragments)
    {
        if (_pdfDoc is null)
            throw new InvalidOperationException("Call LoadFile before FindPassageHits.");

        // Chi phí nằm ở việc strategy của iText gom chữ cho mỗi pattern (parse trang chỉ cỡ 1 ms), nên chạy MỘT strategy
        // với alternation thay vì một strategy cho mỗi cụm; đo ở #246: 4 cụm giảm từ khoảng 75 ms xuống khoảng 16 ms.
        var located = LocateRectsWithText(pageIndex, AccentInsensitiveRegex.BuildAlternation(fragments));

        // Mỗi vùng iText trả mang CẢ chữ của lượt khớp (đã kiểm ở #246: lượt khớp vắt qua hai dòng cho hai vùng, mỗi vùng
        // cùng một chữ đầy đủ), nên map ngược từng lượt khớp về cụm của nó bằng regex neo hai đầu. Alternation chọn nhánh
        // đầu tiên khớp tại vị trí đó, và nhánh đầu tiên khớp nguyên văn chữ khớp được chính là nhánh ấy. Không nối chữ của
        // nhiều lượt khớp lại vì cụm sẽ khớp sai qua chỗ nối.
        var anchored = fragments
            .Select(fragment => AccentInsensitiveRegex.BuildPattern(fragment) is { Length: > 0 } pattern
                ? new Regex($"^(?:{pattern})$", RegexOptions.CultureInvariant, RegexTimeout)
                : null)
            .ToList();
        var found = new bool[fragments.Count];
        foreach (string matchText in located.Select(item => item.Text).Distinct(StringComparer.Ordinal))
        {
            int index = anchored.FindIndex(regex => regex is not null && regex.IsMatch(matchText));
            if (index >= 0) found[index] = true;
        }
        return new PassageHits([.. located.Select(item => item.Rect)], found);
    }

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

    private List<MatchRect> LocateRects(int pageIndex, string pattern)
        => [.. LocateRectsWithText(pageIndex, pattern).Select(item => item.Rect)];

    private List<(MatchRect Rect, string Text)> LocateRectsWithText(int pageIndex, string pattern)
    {
        var result = new List<(MatchRect, string)>();
        if (pattern.Length == 0) return result;
        if (pageIndex < 0 || pageIndex >= _pdfDoc!.GetNumberOfPages()) return result;

        // iText computes the match rectangle from the real glyph layout, so it tracks the keyword
        // exactly (no per-glyph reconstruction guesswork).
        var strategy = new RegexBasedLocationExtractionStrategy(pattern);
        var processor = new PdfCanvasProcessor(strategy);
        var page = _pdfDoc.GetPage(pageIndex + 1);
        processor.ProcessPageContent(page);

        // iText trả toạ độ user space (gốc MediaBox) còn PDFium vẽ phần CropBox, nên trừ gốc CropBox
        // để hộp tính từ góc dưới trái của trang nhìn thấy. Bỏ qua thì hộp lệch đúng bằng độ lệch CropBox.
        var crop = page.GetCropBox();
        foreach (var loc in strategy.GetResultantLocations())
        {
            var r = loc.GetRectangle();
            if (r is null) continue;
            result.Add((new MatchRect(r.GetX() - crop.GetX(), r.GetY() - crop.GetY(), r.GetWidth(), r.GetHeight()), loc.GetText() ?? ""));
        }
        return result;
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
        public List<RawTextItem> Items { get; } = [];

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
