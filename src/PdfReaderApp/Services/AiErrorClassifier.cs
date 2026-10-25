using System.Net.Http;
using Anthropic.Exceptions;
using Anthropic.Models;

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

    public static AiChatError Classify(Exception ex)
    {
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

        if (FindAnthropicException(ex) is { } anthropic)
            return ClassifyAnthropic(anthropic);

        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return AiChatError.Network;

        return AiChatError.Unknown;
    }

    // Phòng thủ: chưa có bằng chứng adapter bọc lỗi SDK Anthropic, nhưng duyệt InnerException không tốn gì.
    private static AnthropicException? FindAnthropicException(Exception ex)
    {
        for (Exception? current = ex; current is not null; current = current.InnerException)
        {
            if (current is AnthropicException anthropic)
                return anthropic;
        }

        return null;
    }

    private static AiChatError ClassifyAnthropic(AnthropicException ex)
    {
        if (ex is AnthropicServiceException { ErrorType: { } errorType } && ClassifyAnthropicErrorType(errorType) is { } byType)
            return byType;

        // Anthropic báo hết tiền bằng HTTP 400 invalid_request_error "Your credit balance is too low", không có ErrorType riêng.
        string detail = ex.Message + " " + (ex as AnthropicApiException)?.ResponseBody + " " + ex.InnerException?.Message;
        if (detail.Contains("credit balance", StringComparison.OrdinalIgnoreCase))
            return AiChatError.InsufficientQuota;

        return ex switch
        {
            AnthropicUnauthorizedException => AiChatError.Unauthorized,
            AnthropicRateLimitException => AiChatError.RateLimit,
            AnthropicIOException => AiChatError.Network,
            _ => AiChatError.Unknown,
        };
    }

    // OverloadedError còn sau khi SDK đã tự thử lại nghĩa là "hãy thử lại sau", cùng cách xử lý với RateLimit.
    private static AiChatError? ClassifyAnthropicErrorType(ErrorType errorType) => errorType switch
    {
        ErrorType.AuthenticationError or ErrorType.PermissionError => AiChatError.Unauthorized,
        ErrorType.RateLimitError or ErrorType.OverloadedError => AiChatError.RateLimit,
        ErrorType.BillingError => AiChatError.InsufficientQuota,
        _ => null,
    };
}
