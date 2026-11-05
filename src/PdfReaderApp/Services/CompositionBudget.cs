namespace PdfReaderApp.Services;

/// <summary>
/// Ngân sách ký tự ngữ cảnh của một Bản soạn theo Chat Model. Một nguồn duy nhất cho hộp thoại tạo (chặn trước khi gửi)
/// và Sinh lại. v1: mọi model dùng <see cref="ContextLimits.MaxChars"/>; model có cửa sổ ngữ cảnh khác thì thêm ở đây.
/// </summary>
internal static class CompositionBudget
{
    public static int CharsFor(string? chatModel) => ContextLimits.MaxChars;
}
