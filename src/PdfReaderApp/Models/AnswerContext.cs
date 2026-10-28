namespace PdfReaderApp.Models;

/// <summary>Ngữ cảnh gửi model cho một câu hỏi, kèm bảng nguồn <c>[n]</c> và các thông báo cho người dùng.</summary>
/// <param name="Text">Ngữ cảnh gửi model.</param>
/// <param name="Sources">Bảng nguồn <c>[n]</c>; rỗng khi lùi về ngữ cảnh liền khối vì không có DocumentId.</param>
/// <param name="Notices">Thông báo theo thứ tự phát sinh, để nơi hiển thị đổi sang chữ.</param>
public sealed record AnswerContext(
    string Text,
    IReadOnlyList<RagSource> Sources,
    IReadOnlyList<AnswerContextNotice> Notices);
