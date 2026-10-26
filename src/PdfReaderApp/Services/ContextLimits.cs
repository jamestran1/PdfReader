namespace PdfReaderApp.Services;

/// <summary>Giới hạn kích thước ngữ cảnh tài liệu gửi cho model; một nguồn dùng chung cho RAG, ngữ cảnh theo trang và chat (CS-5).</summary>
public static class ContextLimits
{
    public const int MaxChars = 48000;
}
