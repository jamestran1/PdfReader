using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>Đoạn nguồn của một trích dẫn cần tô trên trang <see cref="PageIndex"/> (từ 0) của Document
/// <see cref="DocumentId"/>, đã chẻ thành các cụm từ liên tiếp.</summary>
/// <param name="DocumentId">Document chứa nguồn được trích.</param>
/// <param name="PageIndex">Trang (từ 0) của nguồn.</param>
/// <param name="Fragments">Cụm của cả chunk RAG; tô khi không có câu trích hoặc câu trích không tin được.</param>
/// <param name="QuoteFragments">Cụm của câu AI trích nguyên văn; null khi link không kèm câu trích.</param>
/// <remarks>Tô theo từng cụm thay vì cả đoạn: chữ của chunk và chữ iText dựng lại từ trang có thể lệch ở vài chỗ
/// (gạch nối cuối dòng, header chen giữa, thứ tự cột). Lệch một chỗ chỉ làm mất cụm đó, các cụm còn lại vẫn được tô.</remarks>
public sealed record SourcePassage(
    string DocumentId, int PageIndex, IReadOnlyList<string> Fragments, IReadOnlyList<string>? QuoteFragments = null)
{
    public const int WordsPerFragment = 8;

    /// <summary>true khi đoạn này thuộc Document <paramref name="documentId"/>. Mọi viewer theo tab cùng bind một đoạn
    /// nguồn, nên viewer chỉ tô và cuộn khi đoạn thuộc chính Document của nó.</summary>
    public bool BelongsTo(string? documentId) => string.Equals(DocumentId, documentId, StringComparison.Ordinal);

    /// <summary>Tỉ lệ số từ của câu trích phải khớp trên trang để tin câu trích; dưới mức này lùi về tô cả chunk
    /// (một cụm lẻ khớp rất dễ là khớp nhầm chỗ khác trên trang).</summary>
    public const double MinQuoteMatchRatio = 0.5;

    /// <summary>Vùng tô cho <paramref name="source"/>, ưu tiên <paramref name="quote"/> (câu AI dựa vào) và giữ cả chunk để lùi về;
    /// null khi không có gì để tô: nguồn không mang chữ của đoạn (lịch sử chat bản cũ) và không có câu trích.</summary>
    public static SourcePassage? From(RagSource source, string? quote = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        var quoteFragments = string.IsNullOrWhiteSpace(quote) ? null : SplitFragments(quote);
        if (string.IsNullOrWhiteSpace(source.Passage) && quoteFragments is null) return null;
        var chunkFragments = string.IsNullOrWhiteSpace(source.Passage) ? [] : SplitFragments(source.Passage);
        return new SourcePassage(source.DocumentId, source.PageIndex, chunkFragments, quoteFragments);
    }

    // Tính theo số từ, không theo số cụm: cụm cuối có thể chỉ vài từ, khớp riêng nó không chứng minh được câu trích.
    private static int WordCount(string fragment) => fragment.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Tìm vùng tô trên trang. <paramref name="findHits"/> tra một lượt cho nhiều cụm và báo cụm nào có mặt
    /// (mỗi lượt là một lần xử lý cả trang trên UI thread nên câu trích chỉ được tra một lượt, #246);
    /// <paramref name="findRects"/> chỉ lấy vùng, dùng khi tô cả chunk. Các cụm khớp phủ đủ
    /// <see cref="MinQuoteMatchRatio"/> số từ của câu trích thì tô các chỗ khớp của câu trích; ngược lại, hoặc khi không
    /// có câu trích, tra thêm cả chunk và tô nó.</summary>
    public IReadOnlyList<MatchRect> Locate(
        Func<IReadOnlyList<string>, PassageHits> findHits, Func<IReadOnlyList<string>, IReadOnlyList<MatchRect>> findRects)
    {
        ArgumentNullException.ThrowIfNull(findHits);
        ArgumentNullException.ThrowIfNull(findRects);
        if (QuoteFragments is { Count: > 0 } quote)
        {
            var hits = findHits(quote);
            int matchedWords = quote.Where((_, i) => hits.Found[i]).Sum(WordCount);
            if (matchedWords >= quote.Sum(WordCount) * MinQuoteMatchRatio) return hits.Rects;
        }
        return findRects(Fragments);
    }

    /// <summary>Chẻ <paramref name="passage"/> thành các cụm <paramref name="wordsPerFragment"/> từ. Cụm cuối ngắn hơn
    /// nửa cụm thì gộp vào cụm trước: cụm vài từ dễ khớp nhầm chỗ khác trên trang.</summary>
    public static IReadOnlyList<string> SplitFragments(string passage, int wordsPerFragment = WordsPerFragment)
    {
        string[] words = passage.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var fragments = new List<string>();
        for (int start = 0; start < words.Length; start += wordsPerFragment)
        {
            int remaining = words.Length - start;
            int take = Math.Min(wordsPerFragment, remaining);
            if (fragments.Count > 0 && remaining < (wordsPerFragment + 1) / 2)
            {
                fragments[^1] += " " + string.Join(' ', words, start, take);
                break;
            }
            fragments.Add(string.Join(' ', words, start, take));
        }
        return fragments;
    }
}
