namespace PdfReaderApp.Models;

public sealed record MarkdownTaskListItem(bool IsChecked, IReadOnlyList<MarkdownInlineSpan> Spans);
