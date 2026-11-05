namespace PdfReaderApp.Services;

/// <summary>
/// Ngữ cảnh gửi AI. <c>SourceNoteIds[i]</c> là Note có số <c>[i + 1]</c> trong <see cref="Text"/>.
/// <see cref="AdditionalRequest"/> là yêu cầu thêm của người dùng (null nếu không có).
/// </summary>
public sealed record CompositionContext(string Text, IReadOnlyList<string> SourceNoteIds, string? AdditionalRequest = null)
{
    /// <summary>Số ký tự thật sự gửi AI: ngữ cảnh Note cộng yêu cầu thêm. Đây là con số so với ngân sách.</summary>
    public int CharCount => Text.Length + (AdditionalRequest?.Length ?? 0);
}
