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
}
