using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Kiểm tra định dạng key theo provider. Chỉ chặn dạng đã biết là sai, không đoán các dạng khác. Thuần, không ghi log key.</summary>
public static class ApiKeyFormat
{
    // Token đăng nhập gói Claude (OAuth): API trả 401/429 gây hiểu nhầm, nên chặn từ lúc nhập.
    private const string ClaudeSubscriptionTokenPrefix = "sk-ant-oat";

    public const string ClaudeSubscriptionTokenMessage =
        "Đây là token đăng nhập của gói Claude, không dùng được cho API. " +
        "Hãy tạo API key (bắt đầu bằng sk-ant-api) tại console.anthropic.com.";

    /// <summary>Trả về lời báo lỗi tiếng Việt nếu key chắc chắn không dùng được, ngược lại null.</summary>
    public static string? Validate(AiProvider provider, string key) =>
        provider == AiProvider.Claude
        && key.Trim().StartsWith(ClaudeSubscriptionTokenPrefix, StringComparison.Ordinal)
            ? ClaudeSubscriptionTokenMessage
            : null;
}
