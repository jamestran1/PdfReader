namespace PdfReaderApp.Core;

/// <summary>Một khối Markdown đã tách; <c>Level</c> (1 đến 6) chỉ có nghĩa với <see cref="MarkdownBlockKind.Heading"/>.</summary>
public sealed record MarkdownBlock(MarkdownBlockKind Kind, string Text, int Level = 0);
