namespace PdfReaderApp.Models;

/// <summary>Điều người dùng cần biết về cách một Ngữ cảnh trả lời được dựng. Chữ hiển thị do
/// <c>AnswerContextNoticeFormatter</c> quyết định, không nằm ở đây.</summary>
public abstract record AnswerContextNotice
{
    private AnswerContextNotice() { }

    /// <summary>Có <paramref name="Count"/> tài liệu đang được lập chỉ mục ở nơi khác nên chưa vào câu trả lời này.</summary>
    public sealed record IndexingElsewhere(int Count) : AnswerContextNotice;

    /// <summary>Provider embedding thiếu API key nên không truy hồi được; câu trả lời lùi về trang đang đọc.</summary>
    public sealed record MissingEmbeddingKey(AiProvider Provider) : AnswerContextNotice;

    /// <summary>Câu trả lời chỉ dựa trên trang đang đọc dù đang hỏi Toàn Workspace.</summary>
    public sealed record PageOnlyFallback : AnswerContextNotice;
}
