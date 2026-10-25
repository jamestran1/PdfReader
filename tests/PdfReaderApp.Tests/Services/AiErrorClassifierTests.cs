using System.Net;
using System.Net.Http;
using Anthropic.Exceptions;
using Anthropic.Models;
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

    // Constructor public của SDK chỉ nhận HttpRequestException; StatusCode, ResponseBody (required) và ErrorType là init-only nên gán qua reflection.
    private static T Api<T>(HttpStatusCode status, string body = "", ErrorType? errorType = null)
        where T : AnthropicApiException
    {
        var exception = (T)Activator.CreateInstance(typeof(T), new HttpRequestException("boom", null, status))!;
        typeof(AnthropicApiException).GetProperty(nameof(AnthropicApiException.StatusCode))!.SetValue(exception, status);
        typeof(AnthropicApiException).GetProperty(nameof(AnthropicApiException.ResponseBody))!.SetValue(exception, body);
        typeof(AnthropicServiceException).GetProperty(nameof(AnthropicServiceException.ErrorType))!.SetValue(exception, errorType);
        return exception;
    }

    [Theory]
    [InlineData(ErrorType.AuthenticationError, AiChatError.Unauthorized)]
    [InlineData(ErrorType.PermissionError, AiChatError.Unauthorized)]
    [InlineData(ErrorType.RateLimitError, AiChatError.RateLimit)]
    [InlineData(ErrorType.OverloadedError, AiChatError.RateLimit)]
    [InlineData(ErrorType.BillingError, AiChatError.InsufficientQuota)]
    [InlineData(ErrorType.InvalidRequestError, AiChatError.Unknown)]
    public void Classify_AnthropicApiException_UsesErrorType(ErrorType type, AiChatError expected)
    {
        // Loại exception cố tình lệch với ErrorType để chứng minh ErrorType được ưu tiên.
        var ex = Api<AnthropicNotFoundException>(HttpStatusCode.NotFound, errorType: type);
        Assert.Equal(expected, AiErrorClassifier.Classify(ex));
    }

    [Theory]
    [InlineData(ErrorType.OverloadedError, AiChatError.RateLimit)]
    [InlineData(ErrorType.BillingError, AiChatError.InsufficientQuota)]
    [InlineData(ErrorType.AuthenticationError, AiChatError.Unauthorized)]
    public void Classify_AnthropicSseErrorEvent_UsesErrorType(ErrorType type, AiChatError expected)
    {
        var ex = new AnthropicSseException("sse error", new InvalidOperationException()) { ErrorType = type };
        Assert.Equal(expected, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_Anthropic529WithoutErrorType_IsUnknown()
    {
        var ex = Api<Anthropic5xxException>((HttpStatusCode)529, "Overloaded");
        Assert.Equal(AiChatError.Unknown, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_AnthropicUnauthorizedWithoutErrorType_FallsBackToExceptionType()
    {
        var ex = Api<AnthropicUnauthorizedException>(HttpStatusCode.Unauthorized);
        Assert.Equal(AiChatError.Unauthorized, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_AnthropicRateLimitWithoutErrorType_FallsBackToExceptionType()
    {
        var ex = Api<AnthropicRateLimitException>(HttpStatusCode.TooManyRequests);
        Assert.Equal(AiChatError.RateLimit, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_AnthropicCreditBalanceText_IsInsufficientQuota()
    {
        // Anthropic trả HTTP 400 invalid_request_error kèm câu này khi hết credit.
        const string body = "Your credit balance is too low to access the Anthropic API.";
        var ex = Api<AnthropicBadRequestException>(HttpStatusCode.BadRequest, body,
            ErrorType.InvalidRequestError);
        Assert.Equal(AiChatError.InsufficientQuota, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_AnthropicBadRequestOtherwise_IsUnknown()
    {
        var ex = Api<AnthropicBadRequestException>(HttpStatusCode.BadRequest,
            "max_tokens: field required");
        Assert.Equal(AiChatError.Unknown, AiErrorClassifier.Classify(ex));
    }

    [Fact]
    public void Classify_AnthropicIOException_IsNetwork()
    {
        var ex = new AnthropicIOException("không kết nối được", new HttpRequestException("dns"));
        Assert.Equal(AiChatError.Network, AiErrorClassifier.Classify(ex));
    }
}

