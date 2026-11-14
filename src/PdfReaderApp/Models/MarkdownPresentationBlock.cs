namespace PdfReaderApp.Models;

/// <summary>Khối Markdown đã parse an toàn để renderer WPF hiển thị.</summary>
public record MarkdownPresentationBlock(
    MarkdownPresentationBlockKind Kind,
    IReadOnlyList<MarkdownInlineSpan> InlineSpans,
    int Level = 0,
    string? ListMarker = null,
    string Text = "",
    bool IsComplete = true)
{
    protected MarkdownPresentationBlock()
        : this(MarkdownPresentationBlockKind.Paragraph, [], 0, null, "", true)
    {
    }

    public virtual bool Equals(MarkdownPresentationBlock? other) =>
        other is not null &&
        Kind == other.Kind &&
        Level == other.Level &&
        ListMarker == other.ListMarker &&
        Text == other.Text &&
        IsComplete == other.IsComplete &&
        InlineSpans.SequenceEqual(other.InlineSpans);

    public override int GetHashCode() => HashCode.Combine(Kind, Level, ListMarker, Text, IsComplete, InlineSpans.Count);
}
