using PdfReaderApp.Services;

namespace PdfReaderApp.Models;

/// <summary>Tham số dựng ngữ cảnh RAG xuyên Workspace.</summary>
/// <param name="TopK">Số đoạn lấy chung sau khi gộp kết quả mọi tài liệu (không phải mỗi tài liệu).</param>
/// <param name="MaxChars">Ngân sách ký tự của ngữ cảnh.</param>
public sealed record WorkspaceRagOptions(int TopK = 8, int MaxChars = ContextLimits.MaxChars);
