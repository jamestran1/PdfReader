using System;
using System.Collections.Generic;

namespace PdfReaderApp.Services;

public static class SnippetHighlightComputer
{
    public static IReadOnlyList<(string Text, bool IsMatch)> ComputeSegments(string text, string query)
    {
        if (string.IsNullOrEmpty(text)) return new List<(string, bool)>();

        string fq = SearchNormalizer.Fold(query);
        if (fq.Length == 0) return new List<(string, bool)> { (text, false) };

        var (ft, map) = SearchNormalizer.FoldWithMap(text);
        var segs = new List<(string, bool)>();
        int srcPos = 0;
        int search = 0;
        while (search <= ft.Length - fq.Length)
        {
            int idx = ft.IndexOf(fq, search, StringComparison.Ordinal);
            if (idx < 0) break;

            int srcStart = map[idx];
            int srcEnd = map[idx + fq.Length - 1] + 1;
            if (srcStart < srcPos) srcStart = srcPos;

            if (srcStart > srcPos)
                segs.Add((text.Substring(srcPos, srcStart - srcPos), false));
            if (srcEnd > srcStart)
                segs.Add((text.Substring(srcStart, srcEnd - srcStart), true));

            srcPos = srcEnd;
            search = idx + fq.Length;
        }
        if (srcPos < text.Length)
            segs.Add((text.Substring(srcPos), false));

        return segs;
    }
}
