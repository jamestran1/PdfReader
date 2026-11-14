namespace PdfReaderApp.Models;

/// <summary>Một khối Markdown đã parse an toàn để renderer WPF hiển thị.</summary>
public sealed record MarkdownPresentationBlock(
    MarkdownPresentationBlockKind Kind,
    IReadOnlyList<MarkdownInlineSpan> InlineSpans,
    int Level = 0,
    string? ListMarker = null,
    string Text = "",
    bool IsComplete = true)
{
    public bool Equals(MarkdownPresentationBlock? other) =>
        other is not null &&
        Kind == other.Kind &&
        Level == other.Level &&
        ListMarker == other.ListMarker &&
        Text == other.Text &&
        IsComplete == other.IsComplete &&
        InlineSpans.SequenceEqual(other.InlineSpans);

    public override int GetHashCode() => HashCode.Combine(Kind, Level, ListMarker, Text, IsComplete, InlineSpans.Count);
}
