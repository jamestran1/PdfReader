namespace PdfReaderApp.Services;

public enum AiChatError
{
    Unauthorized,
    RateLimit,
    InsufficientQuota,
    Network,
    Unknown
}

public sealed class AiChatException : Exception
{
    public AiChatError Error { get; }

    public AiChatException(AiChatError error, string message, Exception? inner = null)
        : base(message, inner)
    {
        Error = error;
    }
}
