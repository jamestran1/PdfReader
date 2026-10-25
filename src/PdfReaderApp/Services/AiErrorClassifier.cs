using System.Net.Http;

namespace PdfReaderApp.Services;

public static class AiErrorClassifier
{
    public static AiChatError ClassifyStatus(int status) => status switch
    {
        401 => AiChatError.Unauthorized,
        429 => AiChatError.RateLimit,
        _ => AiChatError.Unknown
    };

    /// <summary>
    /// Classifies an HTTP failure, distinguishing a real rate limit from "insufficient_quota"
    /// (OpenAI returns HTTP 429 with that error code when the account has no credits/quota).
    /// </summary>
    public static AiChatError ClassifyResponse(int status, string? detail)
    {
        if (status == 429 && detail is not null
            && detail.Contains("insufficient_quota", StringComparison.OrdinalIgnoreCase))
        {
            return AiChatError.InsufficientQuota;
        }

        return ClassifyStatus(status);
    }

    /// <summary>
    /// Phân loại lỗi Google.GenAI. <see cref="Google.GenAI.ApiException"/> kế thừa <see cref="HttpRequestException"/>
    /// nên phải xét trước nhánh mạng. Gemini trả 400 (không phải 401) khi API key sai (API_KEY_INVALID).
    /// 429 RESOURCE_EXHAUSTED vừa là vượt tốc độ vừa là hết hạn mức: coi là hết hạn mức chỉ khi thông điệp
    /// nhắc quota/billing mà không nhắc giới hạn theo phút hay thời gian thử lại.
    /// </summary>
    public static AiChatError ClassifyGoogle(Google.GenAI.ApiException ex)
    {
        string detail = $"{ex.Status} {ex.Message}";
        return ex.StatusCode switch
        {
            0 => AiChatError.Network,
            401 or 403 => AiChatError.Unauthorized,
            400 when detail.Contains("API_KEY_INVALID", StringComparison.OrdinalIgnoreCase)
                || detail.Contains("API key not valid", StringComparison.OrdinalIgnoreCase) => AiChatError.Unauthorized,
            429 => IsGoogleQuotaExhausted(detail) ? AiChatError.InsufficientQuota : AiChatError.RateLimit,
            503 => AiChatError.RateLimit,
            _ => AiChatError.Unknown,
        };
    }

    private static bool IsGoogleQuotaExhausted(string detail)
    {
        bool perMinute = detail.Contains("PerMinute", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("per minute", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("retry in", StringComparison.OrdinalIgnoreCase);
        bool quota = detail.Contains("quota", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("billing", StringComparison.OrdinalIgnoreCase);
        return quota && !perMinute;
    }

    public static AiChatError Classify(Exception ex)
    {
        if (ex is Google.GenAI.ApiException google)
            return ClassifyGoogle(google);

        // OpenAI SDK surfaces HTTP failures as System.ClientModel.ClientResultException with a Status.
        if (ex is System.ClientModel.ClientResultException cre)
        {
            string detail = cre.Message;
            try { detail += " " + cre.GetRawResponse()?.Content?.ToString(); }
#pragma warning disable CA1031 // best-effort: raw response không phải lúc nào cũng đọc được, mất chi tiết thì vẫn phân loại theo status
            catch (Exception inner) { System.Diagnostics.Trace.TraceWarning($"AiErrorClassifier.Classify: không đọc được raw response: {inner}"); /* raw response not always available */ }
#pragma warning restore CA1031
            return ClassifyResponse(cre.Status, detail);
        }

        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return AiChatError.Network;

        return AiChatError.Unknown;
    }
}
