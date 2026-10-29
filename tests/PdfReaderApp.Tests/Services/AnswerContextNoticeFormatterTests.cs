using PdfReaderApp.Models;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public sealed class AnswerContextNoticeFormatterTests
{
    private const string PageOnlyText =
        "Câu trả lời này chỉ dựa trên trang đang đọc, chưa dùng được các tài liệu khác của Workspace.";

    [Fact]
    public void Format_NoNotices_IsEmpty()
        => Assert.Equal(string.Empty, AnswerContextNoticeFormatter.Format([]));

    [Fact]
    public void Format_IndexingElsewhere_CountsDocuments()
        => Assert.Equal(
            "Khi bạn hỏi, 3 tài liệu đang được lập chỉ mục ở nơi khác nên câu trả lời này chưa gồm tài liệu đó.",
            AnswerContextNoticeFormatter.Format([new AnswerContextNotice.IndexingElsewhere(3)]));

    [Fact]
    public void Format_MissingEmbeddingKey_AsksForTheKeyThenSaysPageOnly()
        => Assert.Equal(
            AiErrorMessages.NotConfigured(AiProvider.OpenAi) + " " + PageOnlyText,
            AnswerContextNoticeFormatter.Format([new AnswerContextNotice.MissingEmbeddingKey(AiProvider.OpenAi)]));

    [Fact]
    public void Format_PageOnlyFallback_SaysAnswerUsesCurrentPageOnly()
        => Assert.Equal(PageOnlyText, AnswerContextNoticeFormatter.Format([new AnswerContextNotice.PageOnlyFallback()]));

    [Fact]
    public void Format_SeveralNotices_JoinsThemWithOneSpaceInOrder()
        => Assert.Equal(
            "Khi bạn hỏi, 1 tài liệu đang được lập chỉ mục ở nơi khác nên câu trả lời này chưa gồm tài liệu đó. " + PageOnlyText,
            AnswerContextNoticeFormatter.Format(
                [new AnswerContextNotice.IndexingElsewhere(1), new AnswerContextNotice.PageOnlyFallback()]));
}
