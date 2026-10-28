using PdfReaderApp.Models;

namespace PdfReaderApp.Controls;

/// <summary>Chữ chú giải của link trích dẫn; tách khỏi <see cref="CitationText"/> để test không cần WPF.</summary>
public static class CitationTooltip
{
    /// <summary>"Tên tài liệu, trang X", thêm câu trích ở dòng dưới khi có; null với chữ thường.</summary>
    public static string? For(CitationSpan span)
    {
        ArgumentNullException.ThrowIfNull(span);
        if (span.Source is not { } source) return null;
        return $"{source.Title}, trang {source.PageIndex + 1}" + (span.Quote is null ? "" : $"\n“{span.Quote}”");
    }
}
