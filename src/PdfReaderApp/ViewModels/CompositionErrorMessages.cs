namespace PdfReaderApp.ViewModels;

/// <summary>Lời báo lỗi khi thao tác với db Bản soạn (xoá, lưu), tách khỏi lỗi gọi AI (CS-5).</summary>
internal static class CompositionErrorMessages
{
    public const string DeleteFailed = "Không xoá được Bản soạn. Thử lại sau.";
    public const string SaveFailed = "Không lưu được Bản soạn. Thử lại sau.";
}
