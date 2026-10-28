using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Hàng đợi lập chỉ mục nền mà <see cref="AnswerContextBuilder"/> đẩy Document còn thiếu index vào.</summary>
#pragma warning disable CA1711 // tên theo Ngữ cảnh trả lời (#277): hàng đợi lập chỉ mục nền thật sự là hàng đợi
public interface IIndexingQueue
{
    /// <summary>Thêm tài liệu vào cuối hàng đợi; tài liệu đã nằm trong hàng đợi thì bỏ qua.</summary>
    void Enqueue(IReadOnlyList<WorkspaceDocument> documents);

    /// <summary>Tài liệu có đang nằm trong hàng đợi (chờ hoặc đang chạy) của chính hàng đợi này không.</summary>
    bool IsQueued(string documentId);
}
#pragma warning restore CA1711
