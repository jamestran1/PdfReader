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

    public static AiChatError Classify(Exception ex)
    {
        // OpenAI SDK surfaces HTTP failures as System.ClientModel.ClientResultException with a Status.
        if (ex is System.ClientModel.ClientResultException cre)
            return ClassifyStatus(cre.Status);

        if (ex is HttpRequestException or TaskCanceledException or TimeoutException)
            return AiChatError.Network;

        return AiChatError.Unknown;
    }
}
