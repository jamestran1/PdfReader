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
            return AiChatError.InsufficientQuota;

        return ClassifyStatus(status);
    }

    public static AiChatError Classify(Exception ex)
    {
        // OpenAI SDK surfaces HTTP failures as System.ClientModel.ClientResultException with a Status.
        if (ex is System.ClientModel.ClientResultException cre)
        {
            string detail = cre.Message;
            try { detail += " " + cre.GetRawResponse()?.Content?.ToString(); }
            catch { /* raw response not always available */ }
            return ClassifyResponse(cre.Status, detail);
        }

        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return AiChatError.Network;

        return AiChatError.Unknown;
    }
}
