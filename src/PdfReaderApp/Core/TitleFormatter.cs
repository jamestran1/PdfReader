using System.Globalization;

namespace PdfReaderApp.Core;

/// <summary>Rút gọn tiêu đề tài liệu để hiện trên tab.</summary>
public static class TitleFormatter
{
    public const string Ellipsis = "…";
    public const string LoadingText = "Đang tải tài liệu";

    public static string Shorten(string title, int maxLength)
    {
        maxLength = Math.Max(maxLength, 0);
        if (title.Length <= maxLength)
        {
            return title;
        }

        // Chỉ cắt ở ranh giới text element để không tách dấu tổ hợp hoặc cặp surrogate.
        var cut = 0;
        foreach (var start in StringInfo.ParseCombiningCharacters(title))
        {
            if (start > maxLength)
            {
                break;
            }

            cut = start;
        }

        return string.Concat(title.AsSpan(0, cut), Ellipsis);
    }

    public static int GetLength(string title) => title.Length;
}
