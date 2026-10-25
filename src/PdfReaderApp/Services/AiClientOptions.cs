using PdfReaderApp.Models;

namespace PdfReaderApp.Services;

/// <summary>Tham số dựng một client AI: provider nào, model nào, key nào.</summary>
public sealed record AiClientOptions(AiProvider Provider, string Model, string ApiKey);
