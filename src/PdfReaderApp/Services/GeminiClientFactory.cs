using Google.GenAI;

namespace PdfReaderApp.Services;

/// <summary>Dựng <see cref="Client"/> Google.GenAI cho Gemini Developer API (dùng API key, không phải Vertex AI).</summary>
internal static class GeminiClientFactory
{
    public static Client Create(string apiKey) => new(vertexAI: false, apiKey: apiKey);
}
