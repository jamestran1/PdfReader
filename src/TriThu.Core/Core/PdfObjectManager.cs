using System;
using System.Collections.Generic;
using PdfReaderApp.Services;
using SkiaSharp;

namespace PdfReaderApp.Core;

public class PdfObjectManager
{
    public class GhostText
    {
        public int PageIndex { get; set; }
        public int CharIndex { get; set; }
        public string Text { get; set; } = string.Empty;
        public SKRect Bounds { get; set; }
    }

    private Dictionary<int, List<GhostText>> _pageTextMap = new();

    public void MapPage(IPdfRenderService pdfService, int pageIndex)
    {
        if (_pageTextMap.ContainsKey(pageIndex)) return;

        var ghosts = new List<GhostText>();
        int charCount = pdfService.GetCharCount(pageIndex);

        for (int i = 0; i < charCount; i++)
        {
            var boundsList = pdfService.GetTextBounds(pageIndex, i, 1);
            if (boundsList.Count > 0)
            {
                ghosts.Add(new GhostText
                {
                    PageIndex = pageIndex,
                    CharIndex = i,
                    Text = pdfService.GetText(pageIndex, i, 1),
                    Bounds = boundsList[0]
                });
            }
        }

        _pageTextMap[pageIndex] = ghosts;
    }

    public GhostText? HitTest(int pageIndex, SKPoint pdfPoint)
    {
        if (!_pageTextMap.TryGetValue(pageIndex, out var ghosts)) return null;

        foreach (var ghost in ghosts)
        {
            if (ghost.Bounds.Contains(pdfPoint))
            {
                return ghost;
            }
        }

        return null;
    }

    public IReadOnlyList<GhostText> GetPageTexts(int pageIndex)
        => _pageTextMap.TryGetValue(pageIndex, out var g) ? g : System.Array.Empty<GhostText>();

    public void Clear()
    {
        _pageTextMap.Clear();
    }
}
