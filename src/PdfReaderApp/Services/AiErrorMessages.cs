using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Lời báo lỗi AI bằng tiếng Việt, một nguồn dùng chung cho chat và lập chỉ mục (CS-5).
/// Trung tính theo provider, trừ thông báo thiếu key gọi đúng tên provider đang dùng.</summary>
public static class AiErrorMessages
{
    public const string Unknown = "Đã xảy ra lỗi không xác định khi gọi AI.";

    public static string NotConfigured(AiProvider provider) =>
        $"Chưa có API key cho {AiModelCatalog.DisplayName(provider)}. Mở Cài đặt AI để nhập key.";

    public static string Describe(AiChatError error) => error switch
    {
        AiChatError.Unauthorized => "API key không hợp lệ, vui lòng kiểm tra lại trong Cài đặt.",
        AiChatError.RateLimit => "Đã vượt giới hạn yêu cầu, vui lòng thử lại sau.",
        AiChatError.InsufficientQuota => "Tài khoản của nhà cung cấp AI không đủ credits. Hãy kiểm tra thanh toán ở trang của nhà cung cấp.",
        AiChatError.Network => "Không kết nối được dịch vụ AI, vui lòng kiểm tra mạng.",
        AiChatError.UnsupportedProvider => "Nhà cung cấp AI đã chọn chưa được hỗ trợ. Vui lòng chọn nhà cung cấp khác trong Cài đặt.",
        _ => "Đã xảy ra lỗi khi gọi AI."
    };
}
