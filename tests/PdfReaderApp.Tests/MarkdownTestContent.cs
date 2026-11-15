using PdfReaderApp.Models;

namespace PdfReaderApp.Tests;

public static class MarkdownTestContent
{
    public static IEnumerable<MarkdownInlineSpan> InlineSpans(MarkdownBlock block) => block switch
    {
        MarkdownTableBlock table => table.Header.SelectMany(cell => cell)
            .Concat(table.Rows.SelectMany(row => row.SelectMany(cell => cell))),
        MarkdownTaskListBlock taskList => taskList.Items.SelectMany(item => item.Spans),
        MarkdownPresentationBlock presentation => presentation.InlineSpans,
        _ => [],
    };
}
