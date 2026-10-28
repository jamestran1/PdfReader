using System.Globalization;
using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Đổi thông báo có kiểu của Ngữ cảnh trả lời sang câu tiếng Việt hiển thị trên ô nhập.</summary>
public static class AnswerContextNoticeFormatter
{
    private const string PageOnlyText =
        "Câu trả lời này chỉ dựa trên trang đang đọc, chưa dùng được các tài liệu khác của Workspace.";

    /// <summary>Nối các thông báo bằng một dấu cách theo thứ tự đã phát sinh; rỗng khi không có thông báo.</summary>
    public static string Format(IReadOnlyList<AnswerContextNotice> notices)
    {
        ArgumentNullException.ThrowIfNull(notices);
        return string.Join(' ', notices.Select(Format));
    }

    private static string Format(AnswerContextNotice notice) => notice switch
    {
        AnswerContextNotice.IndexingElsewhere n => string.Create(CultureInfo.InvariantCulture,
            $"Khi bạn hỏi, {n.Count} tài liệu đang được lập chỉ mục ở nơi khác nên câu trả lời này chưa gồm tài liệu đó."),
        AnswerContextNotice.MissingEmbeddingKey n => $"{AiErrorMessages.NotConfigured(n.Provider)} {PageOnlyText}",
        AnswerContextNotice.PageOnlyFallback => PageOnlyText,
        _ => throw new ArgumentOutOfRangeException(nameof(notice), notice, "Thông báo chưa có chữ hiển thị."),
    };
}
