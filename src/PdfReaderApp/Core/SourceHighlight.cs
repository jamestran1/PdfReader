using PdfReaderApp.Models;

namespace PdfReaderApp.Core;

/// <summary>Đoạn nguồn của một trích dẫn cần tô trên trang <see cref="PageIndex"/> (từ 0) của Document
/// <see cref="DocumentId"/>, đã chẻ thành các cụm từ liên tiếp.</summary>
/// <remarks>Tô theo từng cụm thay vì cả đoạn: chữ của chunk và chữ iText dựng lại từ trang có thể lệch ở vài chỗ
/// (gạch nối cuối dòng, header chen giữa, thứ tự cột). Lệch một chỗ chỉ làm mất cụm đó, các cụm còn lại vẫn được tô.</remarks>
public sealed record SourceHighlight(string DocumentId, int PageIndex, IReadOnlyList<string> Fragments)
{
    public const int WordsPerFragment = 8;

    /// <summary>Vùng tô cho <paramref name="source"/>; null khi nguồn không mang chữ của đoạn (lịch sử chat bản cũ).</summary>
    public static SourceHighlight? From(RagSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (string.IsNullOrWhiteSpace(source.Passage)) return null;
        return new SourceHighlight(source.DocumentId, source.PageIndex, SplitFragments(source.Passage));
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
