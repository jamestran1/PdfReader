namespace PdfReaderApp.Services;

public enum AiChatError
{
    Unauthorized,
    RateLimit,
    InsufficientQuota,
    Network,
    UnsupportedProvider,
    Unknown
}
