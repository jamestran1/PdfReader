using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Danh mục provider và model mặc định. Thuần, bất biến.</summary>
public static class AiModelCatalog
{
    public static IReadOnlyList<AiProvider> ChatProviders { get; } =
        [AiProvider.OpenAi, AiProvider.Gemini, AiProvider.Claude];

    /// <summary>Anthropic không có API embedding nên Claude không nằm trong danh sách.</summary>
    public static IReadOnlyList<AiProvider> EmbeddingProviders { get; } =
        [AiProvider.OpenAi, AiProvider.Gemini];

    public static string DefaultChatModel(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => "gpt-4o-mini",
        AiProvider.Gemini => "gemini-2.5-flash",
        AiProvider.Claude => "claude-sonnet-5-5",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider không được hỗ trợ."),
    };

    public static string DefaultEmbeddingModel(AiProvider provider) => provider switch
    {
        AiProvider.OpenAi => "text-embedding-3-small",
        AiProvider.Gemini => "gemini-embedding-001",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "Provider không có API embedding."),
    };
}
