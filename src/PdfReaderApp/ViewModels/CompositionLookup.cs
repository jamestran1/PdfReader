using PdfReaderApp.Models;

namespace PdfReaderApp.ViewModels;

/// <summary>Tìm Bản soạn của Workspace theo id; trả null khi Bản soạn đã bị xoá (khôi phục Open Set bỏ qua tab đó).</summary>
public delegate Composition? CompositionLookup(string workspaceId, string compositionId);
