using PdfReaderApp.Core;

namespace PdfReaderApp.Models;

/// <summary>Một khối Markdown cùng các đoạn chữ thường và link <c>[n]</c> của nó.</summary>
public sealed record CompositionBlockView(MarkdownBlock Block, IReadOnlyList<CitationSpan> Spans)
{
    public MarkdownBlockKind Kind => Block.Kind;
    public int Level => Block.Level;

    /// <summary>Lề trái của mục danh sách: 8 px cộng 18 px mỗi cấp lồng.</summary>
    public System.Windows.Thickness ListIndent => new(8 + (18 * Block.Level), 0, 0, 0);
}
