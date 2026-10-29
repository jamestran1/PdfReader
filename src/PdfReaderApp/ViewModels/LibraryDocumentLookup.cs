using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Tìm Document trong Library theo id; trả null khi Document đã gỡ khỏi Library.</summary>
public delegate LibraryItem? LibraryDocumentLookup(string documentId);
