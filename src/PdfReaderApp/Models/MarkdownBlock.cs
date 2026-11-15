namespace PdfReaderApp.Models;

/// <summary>Khối Markdown an toàn dùng chung cho renderer.</summary>
public abstract record MarkdownBlock
{
    public abstract MarkdownPresentationBlockKind Kind { get; }
}
