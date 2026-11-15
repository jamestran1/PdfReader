namespace PdfReaderApp.Models;

/// <summary>Khối Markdown đã parse an toàn để renderer WPF hiển thị.</summary>
/// <summary>Khối chữ đơn giản: đoạn, heading, list, quote hoặc code.</summary>
public sealed record MarkdownPresentationBlock(
    MarkdownPresentationBlockKind BlockKind,
    IReadOnlyList<MarkdownInlineSpan> InlineSpans,
    int Level = 0,
    string? ListMarker = null,
    string Text = "",
    bool IsComplete = true,
    string Language = "") : MarkdownBlock
{
    public override MarkdownPresentationBlockKind Kind => BlockKind;

    public bool Equals(MarkdownPresentationBlock? other) =>
        other is not null &&
        Kind == other.Kind &&
        Level == other.Level &&
        ListMarker == other.ListMarker &&
        Text == other.Text &&
        IsComplete == other.IsComplete &&
        Language == other.Language &&
        InlineSpans.SequenceEqual(other.InlineSpans);

    public override int GetHashCode() => HashCode.Combine(Kind, Level, ListMarker, Text, IsComplete, Language, InlineSpans.Count);
}
