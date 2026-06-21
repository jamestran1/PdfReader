using System.Collections.Generic;
using System.Linq;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>
/// Groups per-glyph TextBlocks into logical lines and computes union bounding boxes,
/// enabling phrase-capable, accent-insensitive highlight matching.
/// </summary>
public static class HighlightLineBuilder
{
    /// <summary>A logical line on a PDF page with union bounds across all constituent blocks.</summary>
    public sealed record HighlightLine(string Text, float PdfX, float PdfY, float Width, float Height);

    /// <summary>
    /// Groups <paramref name="pageBlocks"/> (all from the same page) into lines by baseline Y,
    /// ordered left-to-right within each line, and returns one <see cref="HighlightLine"/> per line.
    /// </summary>
    /// <param name="pageBlocks">Blocks that belong to a single page (PageIndex must all be the same).</param>
    public static List<HighlightLine> BuildLines(IReadOnlyList<TextBlock> pageBlocks)
    {
        if (pageBlocks is null || pageBlocks.Count == 0)
            return new List<HighlightLine>();

        // Bucket size: 2pt is tight enough for most text; avoids merging superscripts/subscripts.
        const float bucketPt = 2f;

        // Group by baseline bucket: round PdfY to nearest bucketPt multiple.
        var buckets = new Dictionary<int, List<TextBlock>>();
        foreach (var block in pageBlocks)
        {
            int key = (int)System.MathF.Round(block.PdfY / bucketPt);
            if (!buckets.TryGetValue(key, out var list))
                buckets[key] = list = new List<TextBlock>();
            list.Add(block);
        }

        var lines = new List<HighlightLine>(buckets.Count);
        // Emit lines in descending PdfY order (top of page first; PDF Y grows upward).
        foreach (var kvp in buckets.OrderByDescending(k => k.Key))
        {
            var blocksInLine = kvp.Value.OrderBy(b => b.PdfX).ToList();

            string text = string.Join(" ", blocksInLine.Select(b => b.Text));

            float minX = blocksInLine.Min(b => b.PdfX);
            float maxRight = blocksInLine.Max(b => b.PdfX + b.Width);
            float minY = blocksInLine.Min(b => b.PdfY);
            float maxHeight = blocksInLine.Max(b => b.Height);

            lines.Add(new HighlightLine(
                Text: text,
                PdfX: minX,
                PdfY: minY,
                Width: maxRight - minX,
                Height: maxHeight));
        }

        return lines;
    }
}
