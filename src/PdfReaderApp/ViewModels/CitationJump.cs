using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Mở nguồn của một trích dẫn <c>[n]</c>: Cross-doc jump tới Document và trang của <paramref name="source"/>
/// rồi tô đoạn nguồn trên trang đó.</summary>
public delegate void CitationJump(RagSource source);
