using System.Text.RegularExpressions;

namespace PdfReaderApp.Core;

/// <summary>
/// Tách tập con Markdown mà prompt Bản soạn yêu cầu (tiêu đề <c>#</c>, mục <c>-</c>/<c>*</c>/<c>1.</c>, đoạn văn, <c>**đậm**</c>)
/// thành khối để hiển thị chỉ đọc. Thuần, không phụ thuộc WPF; không thêm thư viện Markdown (ARCH-1).
/// </summary>
public static partial class MarkdownBlockParser
{
    [GeneratedRegex(@"^(?<hashes>#{1,6})\s+(?<text>.*)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"^(?:[-*]|\d+\.)\s+(?<text>.*)$", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 200)]
    private static partial Regex ListItemRegex();

    public static IReadOnlyList<MarkdownBlock> Parse(string markdown)
    {
        var blocks = new List<MarkdownBlock>();
        foreach (string rawLine in (markdown ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0) continue;

            if (HeadingRegex().Match(line) is { Success: true } heading)
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Heading, StripBold(heading.Groups["text"].Value), heading.Groups["hashes"].Length));
            else if (ListItemRegex().Match(line) is { Success: true } item)
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.ListItem, StripBold(item.Groups["text"].Value)));
            else
                blocks.Add(new MarkdownBlock(MarkdownBlockKind.Paragraph, StripBold(line)));
        }

        return blocks;
    }

    private static string StripBold(string text) => text.Replace("**", string.Empty, StringComparison.Ordinal);
}
