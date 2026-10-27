using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Mở nguồn của một trích dẫn <c>[n]</c>: Cross-doc jump tới Document và trang của <paramref name="source"/>
/// rồi tô trên trang đó câu <paramref name="quote"/> AI trích (null: tô cả đoạn nguồn).</summary>
public delegate void CitationOpener(RagSource source, string? quote);
