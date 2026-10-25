using System.Net.Http;
using PdfReaderApp.Services;

namespace PdfReaderApp.Tests.Services;

public class AiErrorClassifierTests
{
    [Theory]
    [InlineData(401, AiChatError.Unauthorized)]
    [InlineData(429, AiChatError.RateLimit)]
    [InlineData(500, AiChatError.Unknown)]
    [InlineData(404, AiChatError.Unknown)]
    public void ClassifyStatus_MapsHttpStatusToError(int status, AiChatError expected)
    {
        Assert.Equal(expected, AiErrorClassifier.ClassifyStatus(status));
    }

    [Fact]
    public void Classify_HttpRequestException_IsNetwork()
    {
        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(new HttpRequestException("boom")));
    }

    [Fact]
    public void Classify_TaskCanceled_IsNetwork()
    {
        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(new TaskCanceledException()));
    }

    [Fact]
    public void Classify_TimeoutException_IsNetwork()
    {
        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(new TimeoutException()));
    }

    [Fact]
    public void Classify_GenericException_IsUnknown()
    {
        Assert.Equal(AiChatError.Unknown, AiErrorClassifier.Classify(new InvalidOperationException()));
    }

    [Fact]
    public void ClassifyResponse_429WithInsufficientQuota_IsInsufficientQuota()
    {
        // OpenAI returns HTTP 429 with code "insufficient_quota" when the account has no credits.
        var detail = "Service request failed. Status: 429. {\"error\":{\"code\":\"insufficient_quota\"}}";
        Assert.Equal(AiChatError.InsufficientQuota, AiErrorClassifier.ClassifyResponse(429, detail));
    }

    [Fact]
    public void ClassifyResponse_429RateLimit_IsRateLimit()
    {
        Assert.Equal(AiChatError.RateLimit,
            AiErrorClassifier.ClassifyResponse(429, "Rate limit reached for requests"));
    }

    [Fact]
    public void ClassifyResponse_429NullDetail_IsRateLimit()
    {
        Assert.Equal(AiChatError.RateLimit, AiErrorClassifier.ClassifyResponse(429, null));
    }

    [Fact]
    public void ClassifyResponse_401_IsUnauthorized()
    {
        Assert.Equal(AiChatError.Unauthorized,
            AiErrorClassifier.ClassifyResponse(401, "insufficient_quota")); // non-429 ignores quota text
    }

    [Theory]
    [InlineData(400, "INVALID_ARGUMENT", "API key not valid. Please pass a valid API key. API_KEY_INVALID", AiChatError.Unauthorized)]
    [InlineData(401, "UNAUTHENTICATED", "Request had invalid authentication credentials.", AiChatError.Unauthorized)]
    [InlineData(403, "PERMISSION_DENIED", "API key not valid or permission denied.", AiChatError.Unauthorized)]
    [InlineData(400, "INVALID_ARGUMENT", "Request contains an invalid argument.", AiChatError.Unknown)]
    [InlineData(429, "RESOURCE_EXHAUSTED", "Quota exceeded for metric generate_content_requests_per_minute PerMinute, retry in 20s", AiChatError.RateLimit)]
    [InlineData(429, "RESOURCE_EXHAUSTED", "You exceeded your current quota, please check your plan and billing details.", AiChatError.InsufficientQuota)]
    [InlineData(503, "UNAVAILABLE", "The model is overloaded. Please try again later.", AiChatError.RateLimit)]
    [InlineData(500, "INTERNAL", "An internal error has occurred.", AiChatError.Unknown)]
    public void Classify_GoogleApiException_MapsStatusAndMessage(int status, string code, string message, AiChatError expected)
    {
        Google.GenAI.ApiException ex = status >= 500
            ? new Google.GenAI.ServerError(message, status, code, null!)
            : new Google.GenAI.ClientError(message, status, code, null!);

        Assert.Equal(expected, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_GoogleApiException_WithoutStatus_IsNetwork()
    {
        var ex = new Google.GenAI.ApiException("connection reset", new HttpRequestException("boom"));

        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(ex));
    }
}
