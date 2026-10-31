using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Một khối Markdown cùng các đoạn chữ thường và link <c>[n]</c> của nó.</summary>
public sealed record CompositionBlockView(MarkdownBlock Block, IReadOnlyList<CitationSpan> Spans)
{
    public MarkdownBlockKind Kind => Block.Kind;
    public int Level => Block.Level;
}
