namespace PdfReaderApp.Models;

[Flags]
public enum MarkdownInlineStyles
{
    None = 0,
    Bold = 1,
    Italic = 2,
    Code = 4,
}
